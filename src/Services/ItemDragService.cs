using System;
using System.IO;
using System.Windows;
using DeskBox.Infrastructure;
using DeskBox.Models;

namespace DeskBox.Services;

public static class ItemDragService
{
    public const string InternalFormat = "DeskBox.ItemId";
    public static bool IsInternal(IDataObject data) => data.GetDataPresent(InternalFormat);
    public static bool IsExternalFileDrop(IDataObject data) => !IsInternal(data) && data.GetDataPresent(DataFormats.FileDrop);

    public static DataObject CreateData(DeskItem item)
    {
        var data = new DataObject();
        data.SetData(InternalFormat, item.Id);
        string path = AppPaths.StoredFilePath(item);
        if (item.Kind == ItemKind.Shortcut && ItemStore.IsShortcut(path) && File.Exists(path))
        {
            data.SetData(DataFormats.FileDrop, new[] { path });
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
        }
        return data;
    }

    public static string? FilePath(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } paths ? paths[0] : null;

    public static bool ReportsMove(IDataObject data, DragDropEffects result)
    {
        if ((result & DragDropEffects.Move) != 0) return true;
        foreach (string format in new[] { "Logical Performed DropEffect", "Performed DropEffect" })
        {
            if (!data.GetDataPresent(format)) continue;
            if (data.GetData(format) is MemoryStream stream && stream.Length >= 4 &&
                (BitConverter.ToInt32(stream.ToArray(), 0) & (int)DragDropEffects.Move) != 0) return true;
        }
        return false;
    }
}
