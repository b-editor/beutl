#include "pch.h"

using namespace System;
using namespace System::Collections::ObjectModel;
using namespace System::IO;
using namespace System::Runtime::Serialization;

namespace BEditor {
	namespace Data {
		namespace ProjectData {
			[DataContract(Namespace = "")]
			public ref class Project : BasePropertyChanged, public IExtensibleDataObject, IDisposable {
			private:
				Scene^ previewSceneProperty;
				bool isDisposed;

			public:
				Project(int width, int height, int framerate);
				~Project();


				event EventHandler^ Disposed;
				event EventHandler^ Disposing;
				static event EventHandler^ Closed;
				static event EventHandler^ Opened;

				property int Framerate;
				property int Samplingrate;
				property String^ FileName;
				property ObservableCollection<Scene^>^ SceneList;
				property int PreviewSceneIndex;
				property String^ GetBackUpName { String^ get(); }
				property Scene^ PreviewScene;
				property bool IsDisposed { bool get(); }

				virtual property ExtensionDataObject^ ExtensionData;
				
				static void BackUp();
				static bool Save(String^ file);
				static bool SaveAs();
				static bool Open(String^ file);
				static void Close();
				static void Create(int width, int height, int framerate, String^ file);
			};
		}
	}
}