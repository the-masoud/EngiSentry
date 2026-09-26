using System;
using System.IO;
using System.Security.Cryptography;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Regression tests for the on-disk SHA-256 calculation while another process holds an open
    /// handle on the same file. AutoCAD keeps the active drawing open for writing, so the hash must
    /// be taken with read/write sharing while remaining read-only itself.
    /// </summary>
    internal static class DrawingHashOpenHandleTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        internal static void Run()
        {
            SharedWithWriterHandle();
            SharedWithReaderHandle();
            ReadOnlyAccessPreserved();
            Console.WriteLine("Open-handle SHA-256 assertions passed: 3 scenarios.");
        }

        // 1. A writer handle is already open (AutoCAD has the drawing loaded) => hash still succeeds
        //    and matches the hash taken without any competing handle.
        private static void SharedWithWriterHandle()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
                string expected = ReferenceSha256(path);

                using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    string actual = HumanEngineeringReviewDecisionFileStore.ComputeSha256(path);
                    Assert(string.Equals(actual, expected, StringComparison.Ordinal), "SHA-256 changed while a writer handle was open.");
                    Assert(actual.Length == 64, "SHA-256 was not 64 hex characters under a writer handle.");
                }
            }
            finally { File.Delete(path); }
        }

        // 2. A reader handle is already open => hash still succeeds and matches the reference hash.
        private static void SharedWithReaderHandle()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 9, 8, 7 });
                string expected = ReferenceSha256(path);

                using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    string actual = HumanEngineeringReviewDecisionFileStore.ComputeSha256(path);
                    Assert(string.Equals(actual, expected, StringComparison.Ordinal), "SHA-256 changed while a reader handle was open.");
                }
            }
            finally { File.Delete(path); }
        }

        // 3. The hash stream must stay read-only: a competing handle opened for exclusive write after
        //    our read starts must not be blocked, and the file bytes must be unchanged by hashing.
        private static void ReadOnlyAccessPreserved()
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] content = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
                File.WriteAllBytes(path, content);
                string before = ReferenceSha256(path);

                string actual = HumanEngineeringReviewDecisionFileStore.ComputeSha256(path);
                Assert(string.Equals(actual, before, StringComparison.Ordinal), "Read-only hash did not match the reference hash.");

                byte[] after = File.ReadAllBytes(path);
                Assert(after.Length == content.Length, "Hashing changed the file length.");
                for (int i = 0; i < content.Length; i++) Assert(after[i] == content[i], "Hashing modified the file contents at offset " + i.ToString() + ".");
            }
            finally { File.Delete(path); }
        }

        private static string ReferenceSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
