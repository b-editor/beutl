#include "pch.h"

using namespace System;
using namespace System::Collections::ObjectModel;
using namespace System::IO;
using namespace System::Runtime::Serialization;

using namespace BEditor::Data::ProjectData;


Project::Project(int width, int height, int framerate) {

}

String^ Project::GetBackUpName::get() {
	return Path::GetDirectoryName(FileName) + "\\" + Path::GetFileNameWithoutExtension(FileName) + ".backup";
}

bool Project::IsDisposed::get() {
	return isDisposed;
}

Scene^ Project::PreviewScene::get() {
	if (previewSceneProperty == nullptr) {
		previewSceneProperty = SceneList[PreviewSceneIndex];
	}
	return previewSceneProperty;
}
void Project::PreviewScene::set(Scene^ value) {
	SetValue(value, previewSceneProperty, "PreviewScene");
	PreviewSceneIndex = SceneList->IndexOf(value);
}