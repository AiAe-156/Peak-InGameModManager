using ModConfigEnhance.Overview;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }

var h = new TextUndoHistory("");
Check(!h.Record("", 0) && !h.CanUndo, "unchanged text records nothing");
h.Record("a", 0); h.Record("ab", .1); h.Record("abc", .2);
Check(h.UndoCount == 1, "fast typing coalesces");
h.Record("abcd", 2);
Check(h.UndoCount == 2, "pause starts a new step");
Check(h.Undo(out var t, out var c) && t == "abc" && c == 3, "undo last step");
Check(h.Undo(out t, out c) && t == "" && c == 0, "undo to empty");
Check(!h.Undo(out _, out _), "nothing left to undo");
Check(h.Redo(out t, out c) && t == "abc" && c == 3, "redo first step");
Check(h.Redo(out t, out c) && t == "abcd" && c == 4, "redo second step");
Check(!h.Redo(out _, out _), "nothing left to redo");
h.Undo(out _, out _); h.Record("abcX", 10);
Check(!h.CanRedo, "new edit clears redo");

h = new TextUndoHistory("hello world");
h.Record("hello wor", 0); h.Record("hello wo", .1);
Check(h.UndoCount == 1 && h.Undo(out t, out c) && t == "hello world" && c == 11, "backspace coalesces and restores");
h = new TextUndoHistory("abcdef");
h.Record("abdef", 0); h.Record("abef", .1);
Check(h.UndoCount == 1 && h.Undo(out t, out _) && t == "abcdef", "forward delete coalesces");
h = new TextUndoHistory("one two");
h.Record("one 2 two", 0);
h.Undo(out t, out c); Check(t == "one two" && c == 4, "middle insert undo caret");
h.Redo(out t, out c); Check(t == "one 2 two" && c == 6, "middle insert redo caret");

h = new TextUndoHistory("");
h.Record("a", 0); h.BreakCoalescing(); h.Record("ab", .1);
Check(h.UndoCount == 2, "break coalescing separates steps");

h = new TextUndoHistory("abc");
h.Record("abXc", 0); h.Record("aXYc", 5);
h.Undo(out t, out _); Check(t == "abXc", "replacement undo");
h.Undo(out t, out _); Check(t == "abc", "second replacement undo");
h.Redo(out t, out _); h.Redo(out t, out _); Check(t == "aXYc", "redo both");

h = new TextUndoHistory("😀x");
h.Record("😁x", 0);
Check(h.Undo(out t, out _) && t == "😀x", "surrogate pair edits are not split");

h = new TextUndoHistory("", maxEntries: 5);
for (int i = 1; i <= 20; i++) h.Record(new string('a', i) + "|" + i, i * 10);
Check(h.UndoCount == 5, "entry limit trims oldest");
h = new TextUndoHistory("", maxChars: 200);
for (int i = 1; i <= 50; i++) h.Record(new string((char)('a' + i % 20), 40) + i, i * 10);
Check(h.UndoCount < 10 && h.UndoCount >= 1, "char budget trims oldest");
int n = 0; while (h.Undo(out _, out _)) n++; Check(n >= 1 && !h.CanUndo, "trimmed history stays consistent");

h = new TextUndoHistory("x"); h.Record("xy", 0); h.Reset("fresh");
Check(!h.CanUndo && !h.CanRedo && h.Text == "fresh", "reset clears history");

Console.WriteLine("TextUndoChecks passed: " + checks);
