namespace Beutl.Api.Clients;

public enum ApiErrorCode
{
    Unknown,

    AuthenticationIsRequired,

    DoNotHavePermissions,

    PackageNotFound,

    PackageNotFoundById,

    PackageIsPrivate,

    UserNotFound,

    UserNotFoundById,

    InvalidPackageName,

    InvalidAssetName,

    InvalidLocaleId,

    InvalidReleaseVersion,

    InvalidRefreshToken,

    InvalidRequestBody,

    AssetMustHaveAtLeastOneHashValue,

    InvalidVersionFormat,

    PackageResourceNotFound,

    PackageResouceHasAlreadyBeenAdded,

    ReleaseNotFound,

    ReleaseNotFoundById,

    CannotPublishAReleaseThatDoesNotHaveAnAsset,

    ReleaseResourceNotFound,

    ReleaseResourceHasAlreadyBeenAdded,

    AssetNotFound,

    AssetNotFoundById,

    RawAssetNotFound,

    NoFilesDataInTheRequest,

    FileIsTooLarge,

    VirtualAssetCannotBeDownloaded,

    CannotDeleteReleaseAssets,
}
