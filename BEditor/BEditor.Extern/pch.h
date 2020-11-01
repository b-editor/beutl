// pch.h: プリコンパイル済みヘッダー ファイルです。
// 次のファイルは、その後のビルドのビルド パフォーマンスを向上させるため 1 回だけコンパイルされます。
// コード補完や多くのコード参照機能などの IntelliSense パフォーマンスにも影響します。
// ただし、ここに一覧表示されているファイルは、ビルド間でいずれかが更新されると、すべてが再コンパイルされます。
// 頻繁に更新するファイルをここに追加しないでください。追加すると、パフォーマンス上の利点がなくなります。

#ifndef PCH_H
#define PCH_H

// プリコンパイルするヘッダーをここに追加します

#define DLLExport(T) extern "C" __declspec(dllexport) T

#include <SDL_ttf.h>
#include <opencv2/opencv.hpp>

#include "FontProcess.h"
#include "FontProperty.h"
#include "FontRender.h"

#include "ImageIO.h"
#include "ImageProperty.h"
#include "ImageEffect.h"
#include "Image.h"

#endif //PCH_H