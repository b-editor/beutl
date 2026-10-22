namespace Beutl;

// An object whose copy must not share a value with it besides the Ids, such as an AI node's request key (#2863).
internal interface ICopyRenewable
{
    // Called on each object of a copy once the copy has Ids of its own.
    void RenewForCopy();
}
