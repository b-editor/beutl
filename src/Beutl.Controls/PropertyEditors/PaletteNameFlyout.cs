namespace Beutl.Controls.PropertyEditors;

// Asks for the name of a palette or of a color in one, with the current name selected so that typing
// replaces it.
internal sealed class PaletteNameFlyout(string header) : TextInputFlyout(header, 240, null, selectAllOnOpening: true);
