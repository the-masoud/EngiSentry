using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using EngiSentry.AutoCAD2021.Scanning;

namespace EngiSentry.AutoCAD2021
{
    public sealed class Commands
    {
        [CommandMethod("ES_HELLO")]
        public void Hello()
        {
            WriteToActiveEditor("\nEngiSentry M0.1\nAutoCAD connection: OK\n");
        }

        [CommandMethod("ENGISENTRY")]
        public void EngiSentry()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null)
                {
                    WriteToActiveEditor("\nEngiSentry\nAutoCAD connection: OK\nDrawing: <none>\nStatus: NO ACTIVE DRAWING\n");
                    return;
                }

                string drawingName = string.IsNullOrWhiteSpace(document.Name)
                    ? "<unnamed>"
                    : System.IO.Path.GetFileName(document.Name);
                document.Editor.WriteMessage(
                    "\nEngiSentry\nAutoCAD connection: OK\nDrawing: {0}\nStatus: READY\n",
                    drawingName);
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry error: {0}\n", exception.Message);
            }
        }

        [CommandMethod("ES_SCAN")]
        public void Scan()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null)
                {
                    WriteToActiveEditor("\nEngiSentry M0.2 Scan Failed\nReason: no active drawing\n");
                    return;
                }

                DrawingScanner.Scan(document, out EngiSentry.Contracts.DrawingSnapshot snapshot);
                string path = SnapshotFileWriter.Write(snapshot);
                int unresolved = 0;
                foreach (EngiSentry.Contracts.XrefSnapshot xref in snapshot.Xrefs) if (!xref.IsResolved) unresolved++;
                document.Editor.WriteMessage("\nEngiSentry M0.2 Scan Complete\nDrawing: {0}\nModel entities: {1}\nPaper entities: {2}\nBlocks: {3}\nBlock references: {4}\nXrefs: {5}\nUnresolved Xrefs: {6}\nSnapshot: {7}\nDBMOD unchanged: {8}\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", snapshot.ModelSpaceEntityCount, snapshot.PaperSpaceEntityCount, snapshot.BlockDefinitions.Count, snapshot.BlockReferenceCount, snapshot.Xrefs.Count, unresolved, path, snapshot.DrawingModifiedByScan ? "NO" : "YES");
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry M0.2 Scan Failed\nReason: {0}\n", exception.Message);
            }
        }

        private static void WriteToActiveEditor(string message, params object[] arguments)
        {
            try
            {
                Editor? editor = Application.DocumentManager?.MdiActiveDocument?.Editor;
                if (editor != null)
                {
                    editor.WriteMessage(message, arguments);
                }
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("EngiSentry command output error: " + exception);
            }
        }
    }
}
