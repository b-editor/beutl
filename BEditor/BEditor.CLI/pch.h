// pch.h: プリコンパイル済みヘッダー ファイルです。
// 次のファイルは、その後のビルドのビルド パフォーマンスを向上させるため 1 回だけコンパイルされます。
// コード補完や多くのコード参照機能などの IntelliSense パフォーマンスにも影響します。
// ただし、ここに一覧表示されているファイルは、ビルド間でいずれかが更新されると、すべてが再コンパイルされます。
// 頻繁に更新するファイルをここに追加しないでください。追加すると、パフォーマンス上の利点がなくなります。

#ifndef PCH_H
#define PCH_H

// プリコンパイルするヘッダーをここに追加します
#include "Exceptions.h"
#include "DisposableObject.h"
#include "DisposableCollection.h"
#include "Size.h"
#include "GLEnums.h"
#include "Enums.h"
#include "Point2.h"
#include "Point3.h"
#include "Rectangle.h"
#include "Range.h"
#include "ImageType.h"
#include "ImageReadMode.h"
#include "FontStyle.h"
#include "Hinting.h"
#include "Color.h"
#include "Font.h"
#include "Image.h"

#include <GL\glew.h>
#include <GL\freeglut.h>
#include <GLFW\glfw3.h>

#include "ImageTypeConverter.h"
#include "GL.h"

#endif //PCH_H
