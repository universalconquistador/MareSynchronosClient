global using AddressBookEntry = (string Name, int World, int City, int Ward, int PropertyType, int Plot, int Apartment, bool ApartmentSubdivision, bool AliasEnabled, string Alias);
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Interop.Ipc;


public enum ResidentialAetheryteKind
{
    Uldah = 9,
    Gridania = 2,
    Limsa = 8,
    Foundation = 70,
    Kugane = 111,
}

public sealed class IpcCallerLifestream : IIpcCaller
{
    private readonly DalamudUtilService _dalamudUtilService;

    private readonly ICallGateSubscriber<IDalamudPlugin> _lifestreamInstance;
    private readonly ICallGateSubscriber<string, object> _lifestreamExecuteCommand;
    private readonly ICallGateSubscriber<AddressBookEntry, object> _lifestreamGoToHousingAddress;
    private readonly ICallGateSubscriber<List<AddressBookEntry>> _lifestreamGetAddressBookEntries;
    private readonly ICallGateSubscriber<Dictionary<string, List<AddressBookEntry>>> _lifestreamGetAddressBookEntriesWithFolders;
    private readonly ICallGateSubscriber<(ResidentialAetheryteKind Kind, int Ward, int Plot)?> _lifestreamGetCurrentPlotInfo;

    private readonly ILogger<IpcCallerLifestream> _logger;

    public IpcCallerLifestream(ILogger<IpcCallerLifestream> logger, IDalamudPluginInterface pluginInterface, DalamudUtilService dalamudUtil, MareMediator mareMediator)
    {
        _logger = logger;
        _dalamudUtilService = dalamudUtil;

        _lifestreamInstance = pluginInterface.GetIpcSubscriber<IDalamudPlugin>("Lifestream.Instance");
        _lifestreamExecuteCommand = pluginInterface.GetIpcSubscriber<string, object>("Lifestream.ExecuteCommand");
        _lifestreamGoToHousingAddress = pluginInterface.GetIpcSubscriber<AddressBookEntry, object>("Lifestream.GoToHousingAddress");
        _lifestreamGetAddressBookEntries = pluginInterface.GetIpcSubscriber<List<AddressBookEntry>>("Lifestream.GetAddressBookEntries");
        _lifestreamGetAddressBookEntriesWithFolders = pluginInterface.GetIpcSubscriber<Dictionary<string, List<AddressBookEntry>>>("Lifestream.GetAddressBookEntriesWithFolders");
        _lifestreamGetCurrentPlotInfo = pluginInterface.GetIpcSubscriber<(ResidentialAetheryteKind Kind, int Ward, int Plot)?>("Lifestream.GetCurrentPlotInfo");

        CheckAPI();
    }

    public bool APIAvailable { get; private set; }

    public void CheckAPI()
    {
        try
        {
            APIAvailable = _lifestreamInstance.HasFunction
                && _lifestreamExecuteCommand.HasAction
                && _lifestreamGetAddressBookEntries.HasFunction
                && _lifestreamGetAddressBookEntriesWithFolders.HasFunction
                && _lifestreamGetCurrentPlotInfo.HasFunction;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed checking Lifestream IPC availability.");
            APIAvailable = false;
        }
    }

    public bool TryExecuteCommand(string command)
    {
        if (!APIAvailable || string.IsNullOrWhiteSpace(command))
            return false;

        try
        {
            _lifestreamExecuteCommand.InvokeAction(command);
            return true;
        }
        catch (IpcNotReadyError ex)
        {
            _logger.LogDebug(ex, "Lifestream ExecuteCommand IPC is not ready.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute Lifestream command.");
            return false;
        }
    }

    public bool TryGoToHousingAddress(AddressBookEntry entry)
    {
        if (!APIAvailable)
            return false;

        try
        {
            _lifestreamGoToHousingAddress.InvokeAction(entry);
            return true;
        }
        catch (IpcNotReadyError ex)
        {
            _logger.LogDebug(ex, "Lifestream GoToHousingAddress IPC is not ready.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to execute Lifestream GoToHousingAddress.");
            return false;
        }
    }

    public List<AddressBookEntry> GetAddressBookEntries()
    {
        if (!APIAvailable)
            return [];

        try
        {
            return _lifestreamGetAddressBookEntries.InvokeFunc();
        }
        catch (IpcNotReadyError ex)
        {
            _logger.LogDebug(ex, "Lifestream GetAddressBookEntries IPC is not ready.");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Lifestream address book entries.");
            return [];
        }
    }

    public Dictionary<string, List<AddressBookEntry>> GetAddressBookEntriesWithFolders()
    {
        if (!APIAvailable)
            return [];

        try
        {
            return _lifestreamGetAddressBookEntriesWithFolders.InvokeFunc().ToDictionary(folder => folder.Key, folder => folder.Value);
        }
        catch (IpcNotReadyError ex)
        {
            _logger.LogDebug(ex, "Lifestream GetAddressBookEntriesWithFolders IPC is not ready.");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Lifestream address book entries with folders.");
            return [];
        }
    }

    public (ResidentialAetheryteKind Kind, int Ward, int Plot)? GetCurrentPlotInfo()
    {
        if (!APIAvailable)
            return null;

        try
        {
            return _lifestreamGetCurrentPlotInfo.InvokeFunc();
        }
        catch (IpcNotReadyError ex)
        {
            _logger.LogDebug(ex, "Lifestream GetCurrentPlotInfo IPC is not ready.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get Lifestream plot info.");
            return null;
        }
    }

    public string GetAddressBookEntryText(AddressBookEntry entry)
    {
        if (entry.PropertyType == 0)
            return $"{_dalamudUtilService.GetWorldName(entry.World)}, {GetResidentialDistrictName(entry.City)}, Ward {entry.Ward}, Plot {entry.Plot}";

        if (entry.PropertyType == 1)
            return $"{_dalamudUtilService.GetWorldName(entry.World)}, {GetResidentialDistrictName(entry.City)}, Ward {entry.Ward}, Apartment {entry.Apartment}";

        return string.Empty;
    }

    public string GetAddressBookEntryTextWithName(AddressBookEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name))
            return $"{GetAddressBookEntryText(entry)} - {entry.Name}";

        return GetAddressBookEntryText(entry);
    }

    private static string GetResidentialDistrictName(ResidentialAetheryteKind kind)
    {
        return kind switch
        {
            ResidentialAetheryteKind.Uldah => "Goblet",
            ResidentialAetheryteKind.Gridania => "Lavender Beds",
            ResidentialAetheryteKind.Limsa => "Mist",
            ResidentialAetheryteKind.Foundation => "Empyreum",
            ResidentialAetheryteKind.Kugane => "Shirogane",
            _ => "Unknown"
        };
    }

    private static string GetResidentialDistrictName(int kind)
    {
        return GetResidentialDistrictName((ResidentialAetheryteKind)kind);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}