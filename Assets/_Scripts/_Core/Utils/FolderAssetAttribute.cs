using UnityEngine;

public class FolderAssetAttribute : PropertyAttribute
{
    public string FolderPath { get; }

    public FolderAssetAttribute(string folderPath)
    {
        FolderPath = folderPath;
    }
}
