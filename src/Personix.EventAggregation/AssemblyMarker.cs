namespace Personix.EventAggregation;

/// <summary>
/// Empty marker type with no members or behaviour. Its only purpose is to give consumers of this
/// package a stable type to anchor reflection against — for example
/// <c>typeof(AssemblyMarker).Assembly</c> — instead of hardcoding this assembly's name.
/// </summary>
public sealed class AssemblyMarker;
