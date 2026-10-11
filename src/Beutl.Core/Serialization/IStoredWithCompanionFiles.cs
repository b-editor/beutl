namespace Beutl.Serialization;

// An object whose stored file comes with files of its own, such as the media fingerprints a scene keeps
// next to its .scene file. StoreToUri writes them before the object's own file, which is what a reader
// opens, so data that moves out of that file reaches its new place first. Inside a storage write
// transaction they are journaled with it.
internal interface IStoredWithCompanionFiles
{
    void StoreCompanionFiles(Uri uri);
}
