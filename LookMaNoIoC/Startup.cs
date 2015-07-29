using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(LookMaNoIoC.Startup))]
namespace LookMaNoIoC
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
