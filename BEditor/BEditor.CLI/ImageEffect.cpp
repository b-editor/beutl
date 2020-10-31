#include "pch.h"

using namespace BEditor::CLI::Media;

inline void Image::Flip(FlipMode mode) {
	ThrowIfDisposed();

	cv::flip(*Ptr, *Ptr, (int)mode);
}

inline void Image::AreaExpansion(int top, int bottom, int left, int right) {
	ThrowIfDisposed();

	cv::copyMakeBorder(*Ptr, *Ptr, top, bottom, left, right, 0); //BORDER_CONSTANT
}
inline void Image::AreaExpansion(int width, int height) {
	int^ v = (height - Height) / 2;
	int^ h = (width - Width) / 2;

	AreaExpansion(*v, *v, *h, *h);
}

inline void Image::Blur(int blurSize, bool alphaBlur) {
	ThrowIfDisposed();

	if (blurSize < 0) throw gcnew ArgumentException("blurSize < 0");
	if (blurSize == 0) return;

	cv::Size size(blurSize, blurSize);

	if (alphaBlur) {
		int width = Width + blurSize;
		int height = Height + blurSize;

		AreaExpansion(width, height);
		cv::blur(*Ptr, *Ptr, size);
	}
	else {
		cv::blur(*Ptr, *Ptr, size);
	}
}
inline void Image::GaussianBlur(int blurSize, bool alphaBlur) {
	ThrowIfDisposed();

	if (blurSize < 0) throw gcnew ArgumentException("blurSize < 0");
	if (blurSize == 0) return;

	if (blurSize % 2 != 1) {
		blurSize++;
	}

	cv::Size size(blurSize, blurSize);

	if (alphaBlur) {
		int width = Width + blurSize;
		int height = Height + blurSize;

		AreaExpansion(width, height);
		cv::GaussianBlur(*Ptr, *Ptr, size, 0.0);
	}
	else {
		cv::GaussianBlur(*Ptr, *Ptr, size, 0.0);
	}
}
inline void Image::MedianBlur(int blurSize, bool alphaBlur) {
	ThrowIfDisposed();

	if (blurSize < 0) throw gcnew ArgumentException("blurSize < 0");
	if (blurSize == 0) return;

	if (blurSize % 2 != 1) {
		blurSize++;
	}

	if (alphaBlur) {
		int width = Width + blurSize;
		int height = Height + blurSize;

		AreaExpansion(width, height);
		cv::medianBlur(*Ptr, *Ptr, blurSize);
	}
	else {
		cv::medianBlur(*Ptr, *Ptr, blurSize);
	}
}

inline void Image::Dilate(int f) {
	if (f < 0) throw gcnew ArgumentException("f < 0");
	if (f == 0) {
		delete Ptr;
		Image^ tmp = gcnew Image();
		Ptr = tmp->Ptr;
		return;
	}
	ThrowIfDisposed();

	cv::dilate(*Ptr, *Ptr, cv::noArray(), cv::Point(-1, -1), f);
}
inline void Image::Erode(int f) {
	if (f < 0) throw gcnew ArgumentException("f < 0");
	if (f == 0) {
		delete Ptr;
		Image^ tmp = gcnew Image();
		Ptr = tmp->Ptr;
		return;
	}
	ThrowIfDisposed();

	cv::erode(*Ptr, *Ptr, cv::noArray(), cv::Point(-1, -1), f);
}

inline void Image::SetColor(Color color) {
	//TODO : ImageEffect[SetColor]
	/*
	ThrowIfDisposed();

            int bitcount = Width * Height * Channels * Type.Bits / 8;

            byte* pixelPtr = DataPointer;
            var step = (int)Step;
            var elemsize = ElemSize;

            Parallel.For(0, Height, y => {
                Parallel.For(0, Width, x => {
                    //ピクセルデータでのピクセル(x,y)の開始位置を計算する
                    //int pos = y * Stride + x * 4;
                    int pos = y * step + x * elemsize;

                    // BGRA
                    pixelPtr[pos] = (byte)color.B;
                    pixelPtr[pos + 1] = (byte)color.G;
                    pixelPtr[pos + 2] = (byte)color.R;
                });
            });

            GC.KeepAlive(this);
	*/
}
inline void Image::Shadow(float x, float y, int blur, float alpha, Color color) {
	//TODO : ImageEffect[Shadow]
	/*
	if (blur < 0) throw new ArgumentException("blur < 0");
            ThrowIfDisposed();

            Image shadow = Clone();
            shadow.Blur(blur, true);
            shadow.SetColor(color);
            ImageHelper.DrawAlpha(shadow, (float)(alpha / 100));

            //キャンバスのサイズ
            int size_w = (int)((Math.Abs(x) + (shadow.Width / 2)) * 2);
            int size_h = (int)((Math.Abs(x) + (shadow.Height / 2)) * 2);

#if UseOpenGL
            ImageHelper.renderer.Clear(size_w, size_h);
            Graphics.Paint(new Point3(x, y, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(shadow));
            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(this));

            shadow.Dispose();

            Native.ImageProcess.Delete(ptr);
            Disposable.Dispose();

            Ptr = new Image(size_w, size_h).Ptr;

            Graphics.GetPixels(this);

            GC.KeepAlive(this);
#else
            var canvas = new Image(size_w, size_h);

            canvas.DrawImage(new Point2(x + (size_w / 2), y + (size_h / 2)), shadow); //影の描画
            canvas.DrawImage(new Point2(size_w / 2, size_h / 2), this);

            shadow.Dispose();
            NativeMethods.HandleException(NativeMethods.core_Mat_delete(Ptr));
            Disposable.Dispose();

            Ptr = canvas.Ptr;

            GC.KeepAlive(this);
#endif
	*/
}
inline void Image::Border(int size, Color color) {
	//TODO : ImageEffect[Border]
	/*
	if (size <= 0) throw new ArgumentException("size <= 0");
            ThrowIfDisposed();

            int nwidth = Width + (size + 5) * 2;
            int nheight = Height + (size + 5) * 2;

#if UseOpenGL
            ImageHelper.renderer.Clear(nwidth, nheight);


            //縁取りを描画
            var mask = Clone();
            mask.SetColor(color);
            mask.AreaExpansion(nwidth, nheight);
            mask.Dilate(size);

            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(mask));

            mask.Dispose();
            Graphics.Paint(new Point3(0, 0, 0), 0, 0, 0, new Point3(0, 0, 0), () => Graphics.DrawImage(this));


            ImageProcess.Delete(Ptr);
            Disposable.Dispose();

            var tmp = new Image(nwidth, nheight);

            Graphics.GetPixels(tmp);
            this.Ptr = tmp.Ptr;

            GC.KeepAlive(this);
#else

            #region OpenCv

                        AreaExpansion(nwidth, nheight);

                        //縁取りを描画
                        var mask = Clone();
                        mask.SetColor(color);
                        mask.Dilate(size);
                        var maskoutptr = mask.OutputArray;

                        //HASK ; 加算合成時に終了する場合がある
                        NativeMethods.HandleException(NativeMethods.core_add(mask.InputArray, InputArray, maskoutptr, IntPtr.Zero, 0));

                        NativeMethods.HandleException(NativeMethods.core_Mat_delete(Ptr));
                        Disposable.Dispose();

                        Ptr = mask.Ptr;

                        NativeMethods.HandleException(NativeMethods.core_OutputArray_delete(maskoutptr));

            #endregion
#endif
	*/
}
inline void Image::Clip(int top, int bottom, int left, int right) {
	if (top < 0 || bottom < 0 || left < 0 || right < 0) throw gcnew ArgumentException();
	ThrowIfDisposed();
	if (Width < left + right || Height < top + bottom) {
		delete Ptr;
		Image^ tmp = gcnew Image();
		Ptr = tmp->Ptr;
		
		return;
	}

	int width = Width - left - right;
	int height = Height - top - bottom;
	int x = left;
	int y = top;

	auto tmp = Clone(Media::Rectangle(x, y, width, height));
	delete Ptr;

	Ptr = tmp->Ptr;
}