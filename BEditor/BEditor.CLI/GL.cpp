#include "pch.h"
#include <GL\glew.h>
#include <GLFW\glfw3.h>

using namespace System;
using namespace BEditor::CLI::Media;

void GetPixels(Image^ image) {
    if (image == nullptr) throw gcnew ArgumentNullException("image");
    image->ThrowIfDisposed();

    glReadBuffer(GL_FRONT);
    
}