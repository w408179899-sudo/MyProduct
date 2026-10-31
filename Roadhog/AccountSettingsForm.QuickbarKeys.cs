namespace Roadhog;

public sealed partial class AccountSettingsForm
{
    private int skillBindingsPreviewRevision;

    private void InvalidateSkillBindingsPreview()
    {
        skillBindingsPreviewRevision++;
        previewSkillBindings = null;
        drinkPreferencePicker?.MarkUnresolved();
        foodPreferencePicker?.MarkUnresolved();
        RefreshAutomaticSkillDisplays();
    }
}
