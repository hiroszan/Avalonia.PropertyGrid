﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Avalonia.PropertyGrid.Controls.Implements
{
    /// <summary>
    /// Class PropertyGridControlFactoryCollection.
    /// Implements the <see cref="Avalonia.PropertyGrid.Controls.IPropertyGridControlFactoryCollection" />
    /// </summary>
    /// <seealso cref="Avalonia.PropertyGrid.Controls.IPropertyGridControlFactoryCollection" />
    internal class PropertyGridControlFactoryCollection : IPropertyGridControlFactoryCollection
    {
        /// <summary>
        /// The factories
        /// </summary>
        readonly List<IPropertyGridControlFactory> _Factories = new List<IPropertyGridControlFactory>();

        /// <summary>
        /// Gets the factories.
        /// </summary>
        /// <value>The factories.</value>
        public IEnumerable<IPropertyGridControlFactory> Factories => _Factories.ToArray();

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyGridControlFactoryCollection"/> class.
        /// </summary>
        public PropertyGridControlFactoryCollection()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PropertyGridControlFactoryCollection"/> class.
        /// </summary>
        /// <param name="factories">The factories.</param>
        public PropertyGridControlFactoryCollection(IEnumerable<IPropertyGridControlFactory> factories)
        {
            _Factories.AddRange(factories);
            _Factories.Sort((x, y) =>
            {
                return Comparer<int>.Default.Compare(y.ImportPriority, x.ImportPriority);
            });
        }

        /// <summary>
        /// Gets the factories.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>IEnumerable&lt;IPropertyGridControlFactory&gt;.</returns>
        public IEnumerable<IPropertyGridControlFactory> GetFactories(object accessToken)
        {
            return _Factories.FindAll(x=>x.Accept(accessToken));
        }

        /// <summary>
        /// Adds the factory.
        /// </summary>
        /// <param name="factory">The factory.</param>
        public void AddFactory(IPropertyGridControlFactory factory)
        {
            _Factories.Add(factory);
            _Factories.Sort((x, y) =>
            {
                return Comparer<int>.Default.Compare(y.ImportPriority, x.ImportPriority);
            });
        }

        /// <summary>
        /// Removes the factory.
        /// </summary>
        /// <param name="factory">The factory.</param>
        public void RemoveFactory(IPropertyGridControlFactory factory)
        {
            _Factories.Remove(factory);
        }
    }
}