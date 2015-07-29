using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace LookMaNoIoC
{
    public interface IMediator
    {
        TResult Handle<TResult>(IRequest<TResult> request);
        Task<TResult> Handle<TResult>(IAsyncRequest<TResult> request);
    }

    public class Mediator : IMediator
    {
        private readonly Func<Type, object> _handlerFactory;

        public Mediator(Func<Type, object> handlerFactory)
        {
            _handlerFactory = handlerFactory;
        }

        public TResult Handle<TResult>(IRequest<TResult> request)
        {
            var genericRequestHandler = typeof(IRequestHandler<,>);
            var handlerType = genericRequestHandler.MakeGenericType(request.GetType(), typeof(TResult));

            var handler = _handlerFactory(handlerType);

            return (TResult)handler.GetType().GetMethod("Handle").Invoke(handler, new object[] { request });
        }

        public async Task<TResult> Handle<TResult>(IAsyncRequest<TResult> request)
        {
            var genericRequestHandler = typeof(IAsyncRequestHandler<,>);
            var handlerType = genericRequestHandler.MakeGenericType(request.GetType(), typeof(TResult));

            var handler = _handlerFactory(handlerType);

            return await (Task<TResult>)handler.GetType().GetMethod("Handle").Invoke(handler, new object[] { request });
        }
    }

    public interface IRequest<TResult> { }
    public interface IAsyncRequest<TResult> { }
    public interface IRequestHandler<TRequest, TResult> where TRequest : IRequest<TResult>
    {
        TResult Handle(TRequest request);
    }

    public interface IAsyncRequestHandler<TRequest, TResult> where TRequest : IAsyncRequest<TResult>
    {
        Task<TResult> Handle(TRequest request);
    }

    public class Unit
    {
        public object Id { get; set; }
    }
    public interface ICommand : IRequest<Unit> { }
    public interface IAsyncCommand : IAsyncRequest<Unit> { }
    public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit> where TCommand : ICommand
    {
    }

    public interface IAsyncCommandHandler<TCommand> : IAsyncRequestHandler<TCommand, Unit> where TCommand : IAsyncCommand
    {
    }
}