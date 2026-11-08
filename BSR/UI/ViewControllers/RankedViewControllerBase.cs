using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;

namespace BSR.UI.ViewControllers
{
    public abstract class RankedViewControllerBase : BSMLAutomaticViewController
    {
        private static readonly Dictionary<Type, string[]> CachedUiProps = new Dictionary<Type, string[]>();

        private string[] GetUiProps()
        {
            var type = GetType();
            if (!CachedUiProps.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => Attribute.IsDefined(p, typeof(UIValue)))
                    .Select(p => p.Name)
                    .ToArray();
                CachedUiProps[type] = props;
            }
            return props;
        }

        public virtual void RefreshUI()
        {
            foreach (string prop in GetUiProps())
            {
                NotifyPropertyChanged(prop);
            }
        }
    }
}