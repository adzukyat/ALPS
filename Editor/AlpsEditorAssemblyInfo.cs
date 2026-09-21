using System.Runtime.CompilerServices;

// The UI tests drive the inspector the way the timeline clip inspector does, which needs
// the attach seam that is not part of the public surface.
[assembly: InternalsVisibleTo("AdzukiSoft.ALPS.EditorUiTests")]
