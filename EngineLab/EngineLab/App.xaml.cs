namespace EngineLab
{
    public partial class App : Application
    {
        // Should the dyno page reload next time it appears?
        public static bool DynoReloadRequested { get; set; }

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}