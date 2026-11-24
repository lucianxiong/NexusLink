
using NexusLink.Views;
namespace NexusLink.Common;

public class SimpleViewLocator : IViewLocator
{
    private static readonly Lazy<SimpleViewLocator> _instance = new(() => new SimpleViewLocator());
    private readonly Dictionary<Type, Func<IViewFor>> _mappings = new();

    private SimpleViewLocator()
    {
        Register<MainWindowViewModel, MainWindow>();
        Register<OptionSettingViewModel, OptionSettingWindow>();
        Register<StatusBarViewModel, StatusBarView>();
    }

    public static SimpleViewLocator Instance => _instance.Value;

    public IViewFor<TViewModel>? ResolveView<TViewModel>() where TViewModel : class
    {
        return ResolveView<TViewModel>(null);
    }

    public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract) where TViewModel : class
    {
        if (_mappings.TryGetValue(typeof(TViewModel), out var factory))
        {
            return factory() as IViewFor<TViewModel>;
        }
        return null;
    }

    public IViewFor? ResolveView(object? instance)
    {
        return ResolveView(instance, null);
    }

    public IViewFor? ResolveView(object? instance, string? contract)
    {
        if (instance == null)
        {
            return null;
        }
        var viewModelType = instance.GetType();
        if (_mappings.TryGetValue(viewModelType, out var factory))
        {
            return factory();
        }
        return null;
    }

    public void Register<TViewModel, TView>(Func<TView> factory)
        where TViewModel : class
        where TView : class, IViewFor<TViewModel>
    {
        _mappings[typeof(TViewModel)] = factory;
    }

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class, IViewFor<TViewModel>, new()
    {
        _mappings[typeof(TViewModel)] = () => new TView();
    }
}
