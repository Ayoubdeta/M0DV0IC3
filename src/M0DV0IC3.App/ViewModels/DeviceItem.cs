namespace M0DV0IC3.App.ViewModels;

/// <summary>Elemento de un desplegable de dispositivos. Dos elementos son iguales si tienen el mismo Id.</summary>
public sealed record DeviceItem(string? Id, string Display, bool IsVirtualCable)
{
    public bool Equals(DeviceItem? other) => other is not null && other.Id == Id;

    public override int GetHashCode() => Id?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => Display;
}
