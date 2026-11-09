namespace CpuTempWidget.Services;

public interface ISettingsStore
{
    Settings Load();
    void Save(Settings settings);
}
