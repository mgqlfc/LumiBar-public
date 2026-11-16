namespace AntigravityFrontend.Services;

// A device read or a XAML/property update never counts as a user volume edit.
public static class VolumeWriteGuard
{
    public static bool CanWrite(bool ready,bool synchronizing,bool loaded,bool enabled,bool userEditing,double value)
        =>ready&&!synchronizing&&loaded&&enabled&&userEditing&&double.IsFinite(value)&&value>=0&&value<=100;
}
