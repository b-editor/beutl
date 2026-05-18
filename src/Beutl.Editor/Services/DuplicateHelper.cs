using System.Collections.Immutable;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public static class DuplicateHelper
{
    public static HashSet<Guid> ExpandWithGroupSiblings(
        IEnumerable<Guid> seedIds,
        IEnumerable<ImmutableHashSet<Guid>> groups)
    {
        var ids = new HashSet<Guid>(seedIds);
        foreach (ImmutableHashSet<Guid> group in groups)
        {
            if (group.Overlaps(ids))
            {
                ids.UnionWith(group);
            }
        }

        return ids;
    }

    // 配置探索のシード範囲を返す。Length は最遅 Start ではなく最遅 Range.End から
    // 算出する: 短い末尾要素が長い先頭要素より早く終わるケースで、検索範囲が
    // 短すぎて元クリップに被る位置を選んでしまう問題を避けるため。
    public static (TimeRange Range, int MinZIndex, int MaxZIndex) ComputePlacementRange(
        IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0)
        {
            throw new ArgumentException("Elements must not be empty.", nameof(elements));
        }

        TimeSpan minStart = elements.Min(e => e.Start);
        int minZIndex = elements.Min(e => e.ZIndex);
        TimeSpan maxEnd = elements.Max(e => e.Range.End);
        int maxZIndex = elements.Max(e => e.ZIndex);

        return (new TimeRange(minStart, maxEnd - minStart), minZIndex, maxZIndex);
    }

    // 複製要素を Scene に配置する。2-phase commit:
    //   Phase 1: 全要素にアンカー基準で位置を割り当て、ディスクへ直列化する。
    //            途中で失敗した場合は書き出し済みファイルを削除して例外を伝播する。
    //   Phase 2: グループ remap と Scene.AddChild を適用する。全要素の I/O が
    //            成功しないと Scene 状態へ反映しないため、孤児ファイル + Scene
    //            未反映の半端状態を回避できる。
    public static void PlaceDuplicates(
        Scene scene,
        Element[] newElements,
        Element[] sourceElements,
        TimeSpan anchorStart,
        int anchorZIndex,
        string elementFileExtension)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(newElements);
        ArgumentNullException.ThrowIfNull(sourceElements);
        ArgumentNullException.ThrowIfNull(elementFileExtension);

        if (newElements.Length == 0) return;
        if (newElements.Length != sourceElements.Length)
        {
            throw new ArgumentException(
                "newElements and sourceElements must have the same length (positional id mapping).",
                nameof(sourceElements));
        }

        if (scene.Uri is null)
        {
            throw new InvalidOperationException(
                "Cannot place duplicates: scene has no Uri. Save the project before duplicating.");
        }

        TimeSpan minStart = newElements.Min(e => e.Start);
        int minZIndex = newElements.Min(e => e.ZIndex);

        var stagedFiles = new List<string>(newElements.Length);
        try
        {
            foreach (Element newElement in newElements)
            {
                newElement.Start = newElement.Start - minStart + anchorStart;
                newElement.ZIndex = newElement.ZIndex - minZIndex + anchorZIndex;

                Uri uri = RandomFileNameGenerator.GenerateUri(scene.Uri, elementFileExtension);
                CoreSerializer.StoreToUri(newElement, uri);
                stagedFiles.Add(uri.LocalPath);
            }
        }
        catch
        {
            // 既に書き出したファイルを best-effort で削除。プロジェクトフォルダに
            // 孤児 .belm ファイルを残さない。削除自体の失敗は元の例外を覆い隠さない
            // ようにスローしない (元の I/O エラーの方が原因として重要)。
            foreach (string path in stagedFiles)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch
                {
                }
            }

            throw;
        }

        var idMapping = new Dictionary<Guid, Guid>(sourceElements.Length);
        for (int i = 0; i < sourceElements.Length; i++)
        {
            idMapping[sourceElements[i].Id] = newElements[i].Id;
        }

        List<ImmutableHashSet<Guid>> newGroups = scene.Groups
            .Select(g => g.Where(id => idMapping.ContainsKey(id))
                .Select(id => idMapping[id])
                .ToImmutableHashSet())
            .Where(g => g.Count >= 2)
            .ToList();
        if (newGroups.Count > 0)
        {
            scene.Groups.AddRange(newGroups);
        }

        foreach (Element newElement in newElements)
        {
            scene.AddChild(newElement);
        }
    }
}
