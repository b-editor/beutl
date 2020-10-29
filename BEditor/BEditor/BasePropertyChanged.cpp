#include "pch.h"

using namespace System;
using namespace System::ComponentModel;
using namespace System::Diagnostics;
using namespace System::Linq;
using namespace System::Linq::Expressions;
using namespace System::Runtime::Serialization;
using namespace BEditor::Data;

generic<typename T> void BasePropertyChanged::SetValue(T src, T% dst, String^ name) {
	if (src == nullptr || !src->Equals(dst)) {
		dst = src;
		RaisePropertyChanged(name);
	}
}

void BasePropertyChanged::RaisePropertyChanged(String^ name) {
	this->PropertyChanged(this, gcnew PropertyChangedEventArgs(name));
}