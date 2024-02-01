namespace Beutl.Models;

// 解像度
public enum FrameCacheScale
{
    // 元のサイズのまま
    Original,

    // プレビュー画面のサイズに合わせてキャッシュする
    Manual,

    Half,

    Quarter,
}
