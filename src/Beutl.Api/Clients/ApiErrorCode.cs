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

    AiPlanRequired,

    AiUsageLimitExceeded,

    AiProviderError,

    AiJobNotFound,

    AiJobIsActive,

    AiJobLimitReached,

    AiRequestInProgress,

    AiRequestWasDeleted,

    /// <summary>
    /// The name this request was sent under belongs to a different request.
    /// Not a malformed body: putting the request back as it was collects what
    /// that name already paid for, and leaving it changed is a new request.
    /// </summary>
    AiRequestChanged,

    AiModelDoesNotSupportRequest,

    AiModelUnavailable,

    AiResultUnavailable,
}
