using System;
using System.Threading.Tasks;

namespace FFXIManager.Infrastructure
{
    public interface IUiDispatcher
    {
        bool CheckAccess();
        void Invoke(Action action);
        void BeginInvoke(Action action);
        Task InvokeAsync(Action action);
        Task<T> InvokeAsync<T>(Func<T> func);
    }
}
