using Firebase.Auth;

using Google.Cloud.Firestore;
using Firebase.Storage;
using SkiaSharp;

namespace BeUtl.Services;

public class PackageController
{
    private readonly AccountService _accountService;

    public PackageController(AccountService accountService)
    {
        _accountService = accountService;
    }

    public FirebaseStorageReference GetPackageImageRef(string packageId, string name)
    {
        return _accountService._storage
            .Child("users")
            .Child(_accountService.User!.Uid)
            .Child("packages")
            .Child(packageId)
            .Child("images")
            .Child(name);
    }

    public async ValueTask<Uri> UploadImage(Stream stream, FirebaseStorageReference reference)
    {
        return new Uri(await reference.PutAsync(stream, default, mimeType: "image/jpeg"));
    }

    public CollectionReference? GetPackages()
    {
        FirestoreDb? db = _accountService._db;
        User? user = _accountService.User;
        if (user == null)
            return null;

        return db.Collection($"users/{user.Uid}/packages");
    }

    public async ValueTask<DocumentReference?> NewPackage()
    {
        FirestoreDb? db = _accountService._db;
        User? user = _accountService.User;
        if (user == null)
            return null;

        CollectionReference colRef = db.Collection($"users/{user.Uid}/packages");
        return await colRef.AddAsync(new
        {
            description = "Description",
            shortDescription = "Short Description",
            displayName = "New Package",
            name = "New Package",
            visible = false
        });
    }
}
