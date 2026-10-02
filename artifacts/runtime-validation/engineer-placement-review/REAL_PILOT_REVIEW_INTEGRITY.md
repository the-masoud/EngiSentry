# Real pilot review integrity

One AutoCAD 2021 Core Console run used `final-review-pilot.scr` and answered `_Y` at quit. Core Console exit was 0; DBMOD was unchanged for both ES_PLOTQA runs and both candidate decisions. Disposable and protected SHA256 values were unchanged.

Protected pilot SHA256 before/after: `7E51EC0BDCE424DEFA46AFC5B63EA63ED4CBEB19A7785A79E0D198DC972BD807`. Disposable SHA256 before/after: same. The protected drawing was not edited. `git diff --check` found no whitespace error.
