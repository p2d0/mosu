using System;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser
{
    /// <summary>
    /// Confirmation dialog for deleting a local profile that has earned performance points.
    /// The confirm button must be held ("Yes. Go for it.").
    /// </summary>
    public partial class DeleteProfileConfirmationDialog : DeletionDialog
    {
        public DeleteProfileConfirmationDialog(string profileName, Action deleteAction)
        {
            BodyText = $"Deleting profile \"{profileName}\" will permanently remove it and all of its scores.";
            DangerousAction = deleteAction;
        }
    }
}
