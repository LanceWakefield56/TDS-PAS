namespace Galenica.Core.Modules;

/// <summary>Raised when module dependencies are missing, circular or would be broken.</summary>
public sealed class ModuleDependencyException(string message) : Exception(message);
