using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace LookMaNoIoC
{

    public class GetAboutMessageCommand: IAsyncRequest<string>
    {

    }

    public class Service : LookMaNoIoC.IService, IDisposable
    {
        private readonly Action<string> _logger;

        public Service(Action<string> logger)
        {
            _logger = logger;
        }

        public string SaySomething()
        {
            return "hello";
        }

        public void Dispose()
        {
            _logger("currently being disposed!");
        }
    }

    public class GetAboutMessageCommandHandler: IAsyncRequestHandler<GetAboutMessageCommand, string>
    {
        private readonly IService _service;

        public GetAboutMessageCommandHandler(IService service)
        {
            _service = service;
        }

        [Log]
        //[Authorize("Admin")]
        public async Task<string> Handle(GetAboutMessageCommand request)
        {
            return await Task.FromResult(_service.SaySomething());
        }
    }

    public class GenericAsyncRequestHandler<TRequest, TResult> : IAsyncRequestHandler<TRequest, TResult> where TRequest : IAsyncRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request)
        {
            return await Task.FromResult<TResult>(default(TResult));
        }
    }

    public class LogAttribute : Attribute { }
    public class AuthorizeAttribute : Attribute {
        private readonly string _roles;

        public AuthorizeAttribute(string roles)
        {
            _roles = roles;
        }

        public string Roles { get { return _roles; } }
    }
}