namespace DatasetStudio.Tests.ExtensionFixtures.SampleExtension.Greeting;

/// <summary>
/// Lives in its own assembly so the loader tests can prove an extension's private dependency
/// resolves from the extension's folder.
/// </summary>
public static class Greeter
{
    /// <summary>Builds the greeting the tests look for.</summary>
    /// <param name="name">Who to greet.</param>
    /// <returns>The greeting.</returns>
    public static string Greet(string name) => $"Hello, {name}, from SampleExtension.Greeting";
}
