#include "pch.h"

using namespace System;
using namespace System::Collections::Generic;
using namespace System::IO;
using namespace System::Linq;
using namespace System::Reflection;
using namespace System::Text;
using namespace System::Threading::Tasks;
using namespace System::Xml::Linq;

using namespace BEditor::Data::ProjectData;
using namespace BEditor::Plugin;

namespace BEditor {
	namespace Data {
		public ref class Component : BasePropertyChanged {
		public:
			property String^ Path {
				String^ get() {
					return path;
				}
			}
			property array<String^> Arguments;
			property List<IPlugin^>^ LoaddedPlugins {
				List<IPlugin^>^ get() {
					return loaddedPlugins;
				}
			}
			property Project^ Project {
				ProjectData::Project^ get() {
					return project;
				}
				void set(ProjectData::Project^ value) {
					SetValue(value, project, "Project");
				}
			}

			static property Component^ Current {
				Component^ get() {
					return current;
				}
			}

		private:
			List<IPlugin^>^ loaddedPlugins = gcnew List<IPlugin^>();
			ProjectData::Project^ project;
			Status status;
			String^ path = System::IO::Path::GetDirectoryName(Assembly::GetEntryAssembly()->Location);

			static Component^ current = gcnew Component();

			Component();
		};

		public enum class Status {
			Idle,
			Edit,
			Saved,
			Playing,
			Pause,
			Output
		};
	}
}