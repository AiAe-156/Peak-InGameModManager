namespace ModConfigEnhance.Overview.Editor;
/// <summary>Document and asset ownership are separate from the shared editor surface and toolbar.</summary>
internal interface IDocumentEditorSource
{
    string RootDirectory { get; }
    string ExportDirectory { get; }
    string? ReadNote { get; }
    Document Load();
    Document ImportMarkdown(string source);
    void Save(Document document);
    string Export();
    string ResolveMedia(string relative);
}
