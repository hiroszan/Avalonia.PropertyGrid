﻿using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.PropertyGrid.Controls.Implements;
using Avalonia.PropertyGrid.Localization;
using Avalonia.PropertyGrid.Model.ComponentModel;
using Avalonia.PropertyGrid.Model.ComponentModel.DataAnnotations;
using Avalonia.PropertyGrid.Model.Extensions;
using Avalonia.PropertyGrid.Model.Services;
using Avalonia.PropertyGrid.Model.Utils;
using Avalonia.PropertyGrid.ViewModels;
using Avalonia.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Avalonia.PropertyGrid.Controls
{
    /// <summary>
    /// Class PropertyGrid.
    /// Implements the <see cref="UserControl" />
    /// </summary>
    /// <seealso cref="UserControl" />
    public partial class PropertyGrid : UserControl
    {
        #region Factories
        /// <summary>
        /// The factories
        /// You can use this fields to extend ability of PropertyGrid
        /// </summary>
        public readonly static ICellEditFactoryCollection FactoryTemplates = new CellEditFactoryCollection();

        /// <summary>
        /// Gets or sets the localization service.
        /// </summary>
        /// <value>The localization service.</value>
        public static ILocalizationService LocalizationService { get; set; } = new InternalLocalizationService();
        #endregion

        #region Properties
        public static readonly StyledProperty<bool> AllowSearchProperty = AvaloniaProperty.Register<PropertyGrid, bool>(nameof(AllowSearch), true);

        public bool AllowSearch 
        { 
            get => GetValue(AllowSearchProperty); set => SetValue(AllowSearchProperty, value);
        }

        public static readonly StyledProperty<PropertyGridShowStyle> ShowStyleProperty = AvaloniaProperty.Register<PropertyGrid, PropertyGridShowStyle>(nameof(ShowStyle), PropertyGridShowStyle.Category);
        public PropertyGridShowStyle ShowStyle
        {
            get => GetValue(ShowStyleProperty); 
            set 
            {
                SetValue(ShowStyleProperty, value);
                ViewModel.ShowCategory = value == PropertyGridShowStyle.Category;
            }
        }

        private object _SelectedObject;
        public static readonly DirectProperty<PropertyGrid, object> SelectedObjectProperty = AvaloniaProperty.RegisterDirect<PropertyGrid, object>(
            nameof(SelectedObject),
            o => o._SelectedObject,
            (o,v)=> o.SetAndRaise(SelectedObjectProperty, ref o._SelectedObject, v)
            );
        public object SelectedObject
        {
            get => _SelectedObject;
            set => SetAndRaise(SelectedObjectProperty, ref _SelectedObject, value);
        }

        PropertyGridViewModel ViewModel = new PropertyGridViewModel();

        public readonly ICellEditFactoryCollection Factories;

        readonly PropertyGridBindingCache Bindings = new PropertyGridBindingCache();

        #endregion

        static PropertyGrid()
        {
            // register builtin factories
            foreach(var type in typeof(PropertyGrid).Assembly.GetTypes())
            {
                if(type.IsClass && !type.IsAbstract && type.IsImplementFrom<ICellEditFactory>())
                {
                    FactoryTemplates.AddFactory(Activator.CreateInstance(type) as ICellEditFactory);
                }
            }

            AllowSearchProperty.Changed.Subscribe(OnAllowSearchChanged);
            ShowStyleProperty.Changed.Subscribe(OnShowStyleChanged);
            SelectedObjectProperty.Changed.Subscribe(OnSelectedObjectChanged);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyGrid"/> class.
        /// </summary>
        public PropertyGrid()
        {
            Factories = new CellEditFactoryCollection(FactoryTemplates.CloneFactories(this));

            this.DataContext = ViewModel;
            ViewModel.PropertyDescriptorChanged += OnPropertyDescriptorChanged;
            ViewModel.FilterChanged += OnFilterChanged;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            InitializeComponent();

            column_name.PropertyChanged += OnColumnNamePropertyChanged;

            Bindings.PropertyChangedEvent += OnBindingPropertyChanged;
        }

        private void OnBindingPropertyChanged(object sender, BindingPropertyChangedEventArgs e)
        {
            if(e.Binding != null && e.Binding.Property != null && e.Binding.Property.IsDefined<ConditionTargetAttribute>())
            {
                SetVisiblity(Bindings);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(ViewModel.ShowCategory))
            {
                ShowStyle = ViewModel.ShowCategory ? PropertyGridShowStyle.Category : PropertyGridShowStyle.Alphabetic;
            }
        }

        private static void OnSelectedObjectChanged(AvaloniaPropertyChangedEventArgs<object> e)
        {
            if(e.Sender is PropertyGrid pg)
            {
                pg.OnSelectedObjectChanged(e.OldValue.Value, e.NewValue.Value);
            }
        }

        private void OnSelectedObjectChanged(object oldValue, object newValue)
        {
            ViewModel.SelectedObject = newValue;
        }

        #region Styled Properties Handler
        private static void OnAllowSearchChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if(e.Sender is PropertyGrid sender)
            {
                sender.OnAllowSearchChanged(e.OldValue, e.NewValue);
            }
        }

        private void OnAllowSearchChanged(object oldValue, object newValue)
        {
            fastFilterBox.IsVisible = (bool)newValue;
            headerGrid.IsVisible = (bool)newValue;
        }

        private static void OnShowStyleChanged(AvaloniaPropertyChangedEventArgs<PropertyGridShowStyle> e)
        {
            if(e.Sender is PropertyGrid sender)
            {
                sender.OnShowStyleChanged(e.OldValue, e.NewValue);
            }
        }

        private void OnShowStyleChanged(Optional<PropertyGridShowStyle> oldValue, BindingValue<PropertyGridShowStyle> newValue)
        {
            BuildPropertiesView(ViewModel.SelectedObject, ViewModel.ShowCategory ? PropertyGridShowStyle.Category : PropertyGridShowStyle.Alphabetic);
        }

        #endregion

        private void OnPropertyDescriptorChanged(object sender, EventArgs e)
        {
            BuildPropertiesView(ViewModel.SelectedObject, ViewModel.ShowCategory ? PropertyGridShowStyle.Category : PropertyGridShowStyle.Alphabetic);
        }

        private void SetVisiblity(IEnumerable<PropertyBinding> bindings)
        {
            // first pass check all direct property binding visibility
            foreach (var binding in bindings)
            {
                if(binding.Property != null && binding.Target != null)
                {
                    binding.Visibility = ViewModel.CheckVisibility(binding.Property, binding.Target);
                }
            }

            // second pass, populate indirect property binding visibility

            if(ViewModel.ShowCategory)
            {
                foreach (var info in bindings)
                {
                    if (info is IndirectPropertyBinding binding && binding.IsCategoryBinding)
                    {
                        binding.PropagateVisiblityState();
                    }
                }
            }
            else
            {
                foreach (var info in bindings)
                {
                    if (info is IndirectPropertyBinding binding)
                    {
                        binding.PropagateVisiblityState();
                    }
                }
            }
        }

        private void OnFilterChanged(object sender, EventArgs e)
        {
            SetVisiblity(Bindings);
        }

        private void BuildPropertiesView(object target, PropertyGridShowStyle propertyGridShowStyle)
        {
            propertiesGrid.RowDefinitions.Clear();
            propertiesGrid.Children.Clear();
            Bindings.Clear();

            ReferencePath referencePath = new ReferencePath();
            
            try
            {
                referencePath.BeginScope(target.GetType().Name);

                if (propertyGridShowStyle == PropertyGridShowStyle.Category)
                {
                    BuildCategoryPropertiesView(target, referencePath);
                }
                else if (propertyGridShowStyle == PropertyGridShowStyle.Alphabetic)
                {
                    BuildAlphabeticPropertiesView(target, referencePath);
                }
            }
            finally
            {
                referencePath.EndScope();
            }

            double width = column_name.Bounds.Width;

            SyncNameWidth(width, false);
        }

        #region Categories
        /// <summary>
        /// Builds the category properties view.
        /// </summary>
        protected virtual void BuildCategoryPropertiesView(object target, ReferencePath referencePath)
        {
            propertiesGrid.ColumnDefinitions.Clear();

            foreach (var categoryInfo in ViewModel.Categories)
            {
                propertiesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                Expander expander = new Expander();
                expander.ExpandDirection = ExpandDirection.Down;
                expander.Header = categoryInfo.Key;
                expander.SetValue(Grid.RowProperty, propertiesGrid.RowDefinitions.Count - 1);
                expander.IsExpanded = true;
                expander.HorizontalContentAlignment = Layout.HorizontalAlignment.Stretch;
                expander.Margin = new Thickness(2);

                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                IndirectPropertyBinding binding = new IndirectPropertyBinding(
                    $"{referencePath.ToString()}[{categoryInfo.Key}]",
                    null,
                    expander,
                    target,
                    referencePath.Count);

                Bindings.AddBinding(binding);

                expander.IsVisible = BuildPropertiesCellEdit(target, referencePath, categoryInfo.Value, expander, grid, binding);

                expander.Content = grid;

                propertiesGrid.Children.Add(expander);
            }

            propertiesGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        /// <summary>
        /// Builds the properties cell edit.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="referencePath">The reference path.</param>
        /// <param name="properties">The properties.</param>
        /// <param name="expander">The expander.</param>
        /// <param name="grid">The grid.</param>
        /// <param name="parentBinding">The parent binding.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool BuildPropertiesCellEdit(
            object target, 
            ReferencePath referencePath, 
            IEnumerable<PropertyDescriptor> properties, 
            Expander expander, 
            Grid grid,
            IndirectPropertyBinding parentBinding
            )
        {
            bool AtLeastOneVisible = false;

            foreach(var property in properties)
            {
                referencePath.BeginScope(property.Name);
                try
                {
                    var value = property.GetValue(target);

                    // if is expand object, expand again will cause overflow exception.
                    if (value != null && !Bindings.IsBinding(value))
                    {
                        var attr = property.GetCustomAttribute<TypeConverterAttribute>();

                        if (attr == null)
                        {
                            attr = property.PropertyType.GetCustomAttribute<TypeConverterAttribute>();
                        }

                        if (attr != null && attr.GetConverterType().IsChildOf<ExpandableObjectConverter>())
                        {
                            AtLeastOneVisible |= BuildExpandableObjectPropertyCellEdit(target, value, referencePath, property, expander, grid, parentBinding);

                            continue;
                        }
                    }

                    AtLeastOneVisible |= BuildPropertyCellEdit(target, referencePath, property, expander, grid, parentBinding);
                }
                finally
                {
                    referencePath.EndScope();
                }
            }

            return AtLeastOneVisible;
        }

        private bool BuildExpandableObjectPropertyCellEdit(object target, object value, ReferencePath referencePath, PropertyDescriptor propertyDescriptor, Expander expander, Grid grid, IndirectPropertyBinding parentBinding)
        {
            Debug.Assert(value != null);

            PropertyDescriptorBuilder builder = new PropertyDescriptorBuilder(value);

            var properties = builder.GetProperties();

            if(properties.Count == 0)
            {
                return false;
            }

            try
            {
                referencePath.BeginScope(value.GetType().Name);

                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                Expander childExpander = new Expander();
                childExpander.ExpandDirection = ExpandDirection.Down;
                childExpander.Header = propertyDescriptor.DisplayName;
                childExpander.SetValue(Grid.RowProperty, grid.RowDefinitions.Count - 1);
                childExpander.SetValue(Grid.ColumnSpanProperty, 2);
                childExpander.IsExpanded = true;
                childExpander.HorizontalAlignment = Layout.HorizontalAlignment.Stretch;
                childExpander.HorizontalContentAlignment = Layout.HorizontalAlignment.Stretch;
                childExpander.Margin = new Thickness(6,2,6,2);

                Grid childGrid = new Grid();
                childGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                childGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                childExpander.Content = childGrid;
                grid.Children.Add(childExpander);

                IndirectPropertyBinding binding = new IndirectPropertyBinding(
                        referencePath.ToString(),
                        propertyDescriptor,
                        childExpander,
                        target,
                        referencePath.Count
                    );

                Bindings.AddBinding(binding);

                parentBinding?.AddBinding(binding);

                binding.Visibility = ViewModel.CheckVisibility(binding.Property, target);

                BuildPropertiesCellEdit(value, referencePath, properties.Cast<PropertyDescriptor>(), childExpander, childGrid, binding);

                binding.PropagateVisiblityState();
            }
            finally
            {
                referencePath.EndScope();
            }
            

            return true;
        }

        /// <summary>
        /// Builds the property cell edit.
        /// </summary>
        /// <param name="target">The target.</param>
        /// <param name="referencePath">The reference path.</param>
        /// <param name="propertyDescriptor">The property descriptor.</param>
        /// <param name="expander">The expander.</param>
        /// <param name="grid">The grid.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        private bool BuildPropertyCellEdit(object target, ReferencePath referencePath, PropertyDescriptor propertyDescriptor, Expander expander, Grid grid, IndirectPropertyBinding parentBinding)
        {
            var property = propertyDescriptor;

            ICellEditFactory factory;
            var control = Factories.BuildPropertyControl(target, property, out factory);

            if (control == null)
            {
#if DEBUG
                Debug.WriteLine($"Failed build property control for property:{property.Name}({property.PropertyType}");
#endif
                return false;
            }

            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            TextBlock nameBlock = new TextBlock();
            nameBlock.Text = LocalizationService[property.DisplayName];
            nameBlock.SetValue(Grid.RowProperty, grid.RowDefinitions.Count - 1);
            nameBlock.SetValue(Grid.ColumnProperty, 0);
            nameBlock.VerticalAlignment = Layout.VerticalAlignment.Center;
            nameBlock.Margin = new Thickness(4);

            if (property.GetCustomAttribute<DescriptionAttribute>() is DescriptionAttribute descriptionAttribute && descriptionAttribute.Description.IsNotNullOrEmpty())
            {
                nameBlock.SetValue(ToolTip.TipProperty, LocalizationService[descriptionAttribute.Description]);
            }

            grid.Children.Add(nameBlock);

            control.SetValue(Grid.RowProperty, grid.RowDefinitions.Count - 1);
            control.SetValue(Grid.ColumnProperty, 1);
            control.IsEnabled = !property.IsReadOnly;
            control.Margin = new Thickness(4);

            grid.Children.Add(control);

            factory.HandlePropertyChanged(target, property, control);

            var binding = new DirectPropertyBinding(referencePath.ToString(), property, expander, target, referencePath.Count, control, nameBlock, factory);
            Bindings.AddBinding(binding);          
            
            parentBinding?.AddBinding(binding);

            binding.Visibility = ViewModel.CheckVisibility(property, target);

            return binding.Visibility == PropertyVisibility.AlwaysVisible;
        }
        #endregion

        #region Alpha
        /// <summary>
        /// Builds the alphabetic properties view.
        /// </summary>
        protected virtual void BuildAlphabeticPropertiesView(object target, ReferencePath referencePath)
        {
            propertiesGrid.ColumnDefinitions.Clear();
            propertiesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            propertiesGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            BuildPropertiesCellEdit(target, referencePath, ViewModel.AllProperties, null, propertiesGrid, null);
        }
        #endregion

        #region Process Widths
        private void SyncWithMaxPropertyNameWidth()
        {
            double maxLength = Bindings.CalcBindingNameMaxLength();

            if(maxLength > 0)
            {
                SyncNameWidth(maxLength, true);
            }            
        }

        private void SyncNameWidth(double width, bool syncToTitle)
        {
            Bindings.SyncWidth(width);

            if (syncToTitle)
            {
                //splitterGrid.ColumnDefinitions[0].Width = new GridLength(width);
                column_name.Width = width;
            }
        }

        private void OnColumnNamePropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if(e.Property == TextBlock.BoundsProperty)
            {
                double width = (sender as TextBlock).Bounds.Width;

                SyncNameWidth(width, false);
            }
        }
        #endregion
    }

    
}
