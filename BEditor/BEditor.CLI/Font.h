#pragma once
#include <SDL_ttf.h>

using namespace System::Runtime::InteropServices;

namespace BEditor {
	namespace CLI {
		namespace Media {
			public ref class Font : DisposableObject {
			public:
				Font(String^ file, int size);
				Font(String^ file, int size, long index);

				property int Size {
					int get() {
						return size;
					};
				}
				property FontStyle Style { FontStyle get(); void set(FontStyle value); }
				property int Outline { int get(); void set(int value); }
				property Media::Hinting Hinting { Media::Hinting get(); void set(Media::Hinting value); }
				property int Height { int get(); }
				property int Ascent { int get(); }
				property int Descent { int get(); }
				property int LineSkip { int get(); }
				property bool Kerning { bool get(); void set(bool value); }
				property long Faces { long get(); }
				property bool IsFixedWidth { bool get(); }
				property String^ FaceFamilyName { String^ get(); }
				property String^ FaceStyleName { String^ get(); }

				int GlyphIsProvided(ushort ch);
				int GlyphIsProvided(uint ch);
				bool GlyphMetrics(ushort ch, [Out] int% minx, [Out] int% maxx, [Out] int% miny, [Out] int% maxy, [Out] int% advance);
				bool GlyphMetrics(uint ch, [Out] int% minx, [Out] int% maxx, [Out] int% miny, [Out] int% maxy, [Out] int% advance);
				bool SizeText(String^ text, [Out] int% width, [Out] int% height);
				bool SizeUnicode(String^ text, [Out] int% width, [Out] int% height);
				bool SizeUTF8(String^ text, [Out] int% width, [Out] int% height);
				void RenderGlyph(uint ch, Color color, [Out] int% width, [Out] int% height, [Out] IntPtr% data);
				void RenderGlyph(ushort ch, Color color, [Out] int% width, [Out] int% height, [Out] IntPtr% data);
				void RenderUnicode(String^ text, Color color, [Out] int% width, [Out] int% height, [Out] IntPtr% data);
				void RenderUTF8(String^ text, Color color, [Out] int% width, [Out] int% height, [Out] IntPtr% data);
				void RenderText(String^ text, Color color, [Out] int% width, [Out] int% height, [Out] IntPtr% data);
				static void Quit();
				static bool Initialize();
			private:
				TTF_Font* font;
				int size;
			protected:
				virtual void OnDispose(bool disposing) override;
			};
		}
	}
}