using GPhotosSyncer.Core;

namespace GPhotosSyncer.App.ViewModels;

/// <summary>One source folder the user added; resolves the actual photos base for display.</summary>
public sealed class SourceItem
{
    public string InputPath { get; }
    public string ResolvedBase { get; }

    public SourceItem(string inputPath)
    {
        InputPath = inputPath;
        ResolvedBase = TakeoutLocator.Resolve(inputPath) ?? inputPath;
    }
}
