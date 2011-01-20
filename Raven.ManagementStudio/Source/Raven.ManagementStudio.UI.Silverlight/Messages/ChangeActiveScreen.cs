namespace Raven.ManagementStudio.UI.Silverlight.Messages
{
    using Plugin;

    public class ChangeActiveScreen
    {
        public ChangeActiveScreen(IRavenScreen screen)
        {
            this.ActiveScreen = screen;
        }

        public IRavenScreen ActiveScreen { get; private set; }
    }
}