// PluginBuilder.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Property.Easing;
using BEditor.Media;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a class that initializes the services provided by the <see cref="PluginObject"/>.
    /// </summary>
    public sealed class PluginBuilder
    {
        private readonly Func<PluginObject> _plugin;
        private readonly List<EffectMetadata> _effects = new();
        private readonly List<ObjectMetadata> _objects = new();
        private readonly List<EasingMetadata> _eases = new();
        private readonly List<PluginTask> _task = new();
        private readonly List<(string, IEnumerable<BasePluginMenu>)> _menus = new();
        private readonly List<FileMenu> _fileMenus = new();

        private PluginBuilder(Func<PluginObject> create)
        {
            _plugin = create;
        }

        /// <summary>
        /// Gets or sets the plugin config.
        /// </summary>
        internal static PluginConfig? Config { get; set; }

        /// <summary>
        /// Begin configuring an <see cref="PluginObject"/>.
        /// </summary>
        /// <typeparam name="T">Class that implements the <see cref="PluginObject"/> to be configure.</typeparam>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public static PluginBuilder Configure<T>()
            where T : PluginObject
        {
            return new PluginBuilder(() => (T)Activator.CreateInstance(typeof(T), Config)!);
        }

        /// <summary>
        /// Configure the options for the services to be provided.
        /// </summary>
        /// <param name="metadata">Metadata of the effects to be provided.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder With(EffectMetadata metadata)
        {
            _effects.Add(metadata);

            return this;
        }

        /// <summary>
        /// Configure the options for the services to be provided.
        /// </summary>
        /// <param name="metadata">Metadata of the objects to be provided.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder With(ObjectMetadata metadata)
        {
            _objects.Add(metadata);

            return this;
        }

        /// <summary>
        /// Configure the options for the services to be provided.
        /// </summary>
        /// <param name="metadata">Metadata of the easings to be provided.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder With(EasingMetadata metadata)
        {
            _eases.Add(metadata);

            return this;
        }

        /// <summary>
        /// Add an encoding.
        /// </summary>
        /// <param name="decoding">The decoding to be added.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder With(IRegisterdDecoding decoding)
        {
            DecodingRegistory.Register(decoding);

            return this;
        }

        /// <summary>
        /// Add an encoding.
        /// </summary>
        /// <param name="encoding">The encoding to be added.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder With(IRegisterdEncoding encoding)
        {
            EncodingRegistory.Register(encoding);

            return this;
        }

        /// <summary>
        /// Add a task to be executed when the application is launched.
        /// </summary>
        /// <param name="task">The background task to run.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder Task(PluginTask task)
        {
            if (!_task.Contains(task))
            {
                _task.Add(task);
            }

            return this;
        }

        /// <summary>
        /// Add a task to be executed when the application is launched.
        /// </summary>
        /// <param name="func">The function to run.</param>
        /// <param name="name">The name of the <see cref="PluginTask"/>.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder Task(Func<IProgressDialog, ValueTask> func, string name)
        {
            var task = new PluginTask(func, name);
            if (!_task.Contains(task))
            {
                _task.Add(task);
            }

            return this;
        }

        /// <summary>
        /// Set the menu.
        /// </summary>
        /// <param name="header">The string to display in the menu header.</param>
        /// <param name="menus">Menu to be set.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        [Obsolete("To be added.")]
        public PluginBuilder SetCustomMenu(string header, IEnumerable<ICustomMenu> menus)
        {
            _menus.Add((header, menus.Select(i => new PluginMenuImpl(i)).ToArray()));

            return this;
        }

        /// <summary>
        /// Add the menu.
        /// </summary>
        /// <param name="header">The string to display in the menu header.</param>
        /// <param name="menus">Menu to be set.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder PluginMenu(string header, params BasePluginMenu[] menus)
        {
            _menus.Add((header, menus));

            return this;
        }

        /// <summary>
        /// Add the file menu.
        /// </summary>
        /// <param name="menus">Menu to be set.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder FileMenu(FileMenu menus)
        {
            _fileMenus.Add(menus);

            return this;
        }

        /// <summary>
        /// Register services into the <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="configureServices">A delegate for configuring the <see cref="IServiceCollection"/>.</param>
        /// <returns>The same instance of the <see cref="PluginBuilder"/> for chaining.</returns>
        public PluginBuilder ConfigureServices(Action<IServiceCollection> configureServices)
        {
            configureServices.Invoke(Config!.Application.Services);

            return this;
        }

        /// <summary>
        /// Register this setting to the specified <see cref="PluginManager"/>.
        /// </summary>
        /// <param name="manager"><see cref="PluginManager"/> to register.</param>
        public void Register(PluginManager manager)
        {
            var instance = _plugin();

            // Effects
            foreach (var meta in _effects)
            {
                EffectMetadata.LoadedEffects.Add(meta);
            }

            // Objects
            foreach (var meta in _objects)
            {
                ObjectMetadata.LoadedObjects.Add(meta);
            }

            // Easing
            foreach (var meta in _eases)
            {
                EasingMetadata.LoadedEasingFunc.Add(meta);
            }

            // プラグインメニューを追加
            manager.Menus.AddRange(_menus);

            // ファイルメニューを追加
            if (_fileMenus.Count > 0)
                manager.FileMenus.Add((instance.GetType().Assembly, _fileMenus));

            if (_task.Count is not 0)
            {
                manager.Tasks.Add((instance, _task));
            }

            manager.Loaded.Add(instance);
        }

        /// <summary>
        /// Register this setting to the <see cref="PluginManager.Default"/>.
        /// </summary>
        public void Register()
        {
            Register(PluginManager.Default);
        }

        [Obsolete("To be added.")]
        private sealed class PluginMenuImpl : BasePluginMenu
        {
            private readonly ICustomMenu _menu;

            public PluginMenuImpl(ICustomMenu menu)
            {
                _menu = menu;
                Name = menu.Name;
            }

            protected override void OnExecute()
            {
                _menu.Execute();
            }
        }
    }
}