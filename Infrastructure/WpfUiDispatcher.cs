using System;
using System.Threading.Tasks;
using System.Windows;

namespace FFXIManager.Infrastructure
{
    public class WpfUiDispatcher : IUiDispatcher
    {
        public bool CheckAccess()
        {
            return Application.Current?.Dispatcher?.CheckAccess() ?? false;
        }

        public void Invoke(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        public void BeginInvoke(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                action();
                return;
            }
            dispatcher.BeginInvoke(action);
        }

        public Task InvokeAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return dispatcher.InvokeAsync(action).Task;
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return Task.FromResult(func());
            }
            return dispatcher.InvokeAsync(func).Task;
        }
    }
}
