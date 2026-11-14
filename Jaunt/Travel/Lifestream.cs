using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;
using Jaunt.Core.Text;

// Lifestream's AddressBookEntryTuple (Lifestream/Data/AddressBookEntry.cs):
// Name, World, City, Ward, PropertyType, Plot, Apartment, ApartmentSubdivision, AliasEnabled, Alias
using AddressBookEntry = (string, int, int, int, int, int, int, bool, bool, string);

namespace Jaunt.Travel;

/// <summary>
/// Travels to a housing address through Lifestream's IPC: <c>BuildAddressBookEntry</c> turns the
/// address into an address-book entry, and <c>GoToHousingAddress</c> goes there. Lifestream is
/// optional; without it, the listing window offers Copy address instead (risk <c>de04e687</c>).
/// </summary>
internal sealed class Lifestream(IDalamudPluginInterface pluginInterface, IPluginLog log)
{
    private readonly ICallGateSubscriber<string, string, string, string, bool, bool, AddressBookEntry> build =
        pluginInterface.GetIpcSubscriber<string, string, string, string, bool, bool, AddressBookEntry>("Lifestream.BuildAddressBookEntry");

    private readonly ICallGateSubscriber<AddressBookEntry, object> goTo =
        pluginInterface.GetIpcSubscriber<AddressBookEntry, object>("Lifestream.GoToHousingAddress");

    private readonly ICallGateSubscriber<bool> isBusy =
        pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");

    /// <summary>Whether Lifestream is loaded and offers the calls travel needs.</summary>
    public bool Available => build.HasFunction && goTo.HasAction && isBusy.HasFunction;

    /// <summary>Starts travelling to the address.</summary>
    /// <returns>Null when travel started, or why it didn't, to show the player.</returns>
    public string? Travel(TravelRequest request)
    {
        try
        {
            if (isBusy.InvokeFunc())
                return "Lifestream is busy. Wait for it to finish, then try again.";

            // Lifestream throws when it can't read the address, rather than returning nothing
            var entry = build.InvokeFunc(request.World, request.District, request.Ward, request.Number, request.IsApartment, request.IsSubdivision);
            goTo.InvokeAction(entry);
            return null;
        }
        catch (IpcNotReadyError)
        {
            return "Lifestream isn't loaded.";
        }
        catch (Exception e)
        {
            log.Warning(e, $"Lifestream couldn't travel to {request}");
            return "Lifestream couldn't travel to this address. Copy it and travel by hand.";
        }
    }
}
