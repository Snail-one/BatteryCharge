namespace BatteryCharge.Core;

/// <summary>A single 32-bit request to the manufacturer's energy device.</summary>
public interface IEnergyTransport
{
    uint Query(uint controlCode, uint input);
    void Send(uint controlCode, uint input);
}
