using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace AutoGrading.ViewModel
{
    public static class FocusExtension
    {
        public static readonly DependencyProperty LoadedFocusedElementProperty =
            DependencyProperty.RegisterAttached("LoadedFocusedElement", typeof(IInputElement), typeof(FocusExtension),
                                                new PropertyMetadata(OnLoadedFocusedElementChanged));

        public static IInputElement GetLoadedFocusedElement(DependencyObject element)
        {
            return (IInputElement)element.GetValue(LoadedFocusedElementProperty);
        }

        public static void SetLoadedFocusedElement(DependencyObject element, bool value)
        {
            element.SetValue(LoadedFocusedElementProperty, value);
        }

        private static void OnLoadedFocusedElementChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            var element = (FrameworkElement)obj;

            var oldFocusedElement = (IInputElement)e.OldValue;
            if (oldFocusedElement != null)
            {
                element.Loaded -= LoadedFocusedElement_Loaded;
            }

            var newFocusedElement = (IInputElement)e.NewValue;
            if (newFocusedElement != null)
            {
                element.Loaded += LoadedFocusedElement_Loaded;
            }
        }

        private static void LoadedFocusedElement_Loaded(object sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            var focusedElement = GetLoadedFocusedElement(element);
            focusedElement.Focus();
        }
    }
}
