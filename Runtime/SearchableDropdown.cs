using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kiryonn.Searchables
{
	/// <summary>
	/// A searchable dropdown field that can be used in the Unity UI Toolkit.
	/// It provides a searchable list of choices and can be bound to a string property.
	/// </summary>
	[UxmlElement("searchable-dropdown")]
	public partial class SearchableDropdown : BaseField<SearchableDropdown.State>
	{
		public struct State
		{
			public int index;
			public string value;

			public State(int index, string value)
			{
				this.index = index;
				this.value = value;
			}
		}

		/// <summary>
		/// All possible choices from the dropdown.
		/// </summary>
		private string[] _choices = Array.Empty<string>();

		/// <summary>
		/// The list of choices available in the dropdown.
		/// </summary>
		/// <remarks>Directly modifying elements doesn't do anything. Assign a new list instead.</remarks>
		public string[] choices
		{
			get => _choices.ToArray();
			set => SetChoices(value);
		}

		/// <summary>
		/// The current state of the dropdown, (selected index and value).
		/// </summary>
		private State _state = new(-1, null); // Default state

		/// <summary>
		/// The name of the seleted item.
		/// </summary>
		public new string value // base member type == State
		{
			get => _state.value;
			set => SetIndex(Array.IndexOf(_choices, value));
		}

		/// <summary>
		/// The index in <see cref="choices"/> of the selected item.
		/// </summary>
		public int index
		{
			get => _state.index;
			set => SetIndex(value);
		}

		// UI elements.
		private Button _mainButton;
		private TextField _searchField;
		private ScrollView _scrollView;

		// Internal UI state.
		private bool _dropdownVisible = false;
		private readonly Dictionary<string, string[]> _searchCache = new();
		private string[] _filteredChoices;
		private int _loadedItemCount;
		private Vector2 _mousePosition;
		/// <summary>Used to optimize search by comparing it with new filter text.</summary>
		public string _filterText = null;

		/// <summary>The maximum number of items to show in the dropdown list.</summary>
		[UxmlAttribute("max-visible-items")]
		public int MaxVisibleItems = 50;

		/// <summary>The comma-separated list of choices from UXML.</summary>
		[UxmlAttribute("choices")]
		public string ChoicesString
		{
			get => string.Join(",", _choices);
			set
			{
				if (!string.IsNullOrEmpty(value))
				{
					var choices = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToArray();
					SetChoices(choices);
				}
			}
		}

		private SearchableDropdown(string label, IEnumerable<string> choices, VisualElement container) : base(label, container)
		{
			AddToClassList(ussClassName);
			labelElement.style.width = 20;

			container.style.flexDirection = FlexDirection.Column;
			container.AddToClassList("unity-base-field__input");
			Add(container);

			_mainButton = new Button(OnMainButtonPressed) { text = value };
			_mainButton.AddToClassList("unity-button");
			container.Add(_mainButton);

			_searchField = new TextField { style = { display = DisplayStyle.None } };
			_searchField.RegisterCallback<ChangeEvent<string>>(OnSearchTextChanged);
			container.Add(_searchField);

			_scrollView = new ScrollView { style = { display = DisplayStyle.None, maxHeight = 200 } };
			_scrollView.verticalScroller.valueChanged += OnScroll;
			container.Add(_scrollView);

			if (choices != null)
			{
				SetChoices(choices.ToArray());
			}

			UpdateMainButtonText();

			// events to track the mouse position
			_scrollView.RegisterCallback<PointerMoveEvent>(OnMousePositionChanged);
			_scrollView.RegisterCallback<PointerDownEvent>(OnMousePositionChanged);
		}
		public SearchableDropdown() : this(null, null, new VisualElement()) { }
		public SearchableDropdown(string label = null, IEnumerable<string> choices = null) : this(label, choices, new VisualElement()) { }

		private void OnMousePositionChanged<T>(T evt) where T : PointerEventBase<T>, new()
		{
			_mousePosition = evt.position;
			evt.StopPropagation();
		}

		private void OnFocusOut(FocusOutEvent evt)
		{
			if (!_scrollView.ContainsPoint(_mousePosition))
			{
				HideDropdown();
			}
		}

		private void OnMainButtonPressed()
		{
			ShowDropdown();
		}

		private void SetIndex(int newIndex)
		{
			if (_state.index == newIndex) return;

			// Reset to no selection if out of bounds
			if (newIndex < -1 || newIndex >= _choices.Length)
			{
				newIndex = -1;
			}

			var oldState = _state;
			_state = new State(newIndex, newIndex >= 0 ? _choices[newIndex] : null);

			UpdateMainButtonText();

			// Invoke the BaseField's change event
			using (var evt = ChangeEvent<State>.GetPooled(oldState, _state))
			{
				evt.target = this;
				SendEvent(evt);
			}
		}

		private void UpdateMainButtonText()
		{
			_mainButton.text = value;
		}

		private void SetChoices(string[] newValue)
		{
			_choices = newValue.ToArray();
			_searchCache.Clear();
			FilterAndPopulateDropdown(string.Empty);
			UpdateMainButtonText();
		}


		private void ShowDropdown()
		{
			if (_dropdownVisible) return;
			_dropdownVisible = true;
			_mainButton.style.display = DisplayStyle.None;
			_searchField.style.display = DisplayStyle.Flex;
			_scrollView.style.display = DisplayStyle.Flex;
			FilterAndPopulateDropdown(_searchField.value);
			_searchField.Focus();
			_searchField.RegisterCallback<FocusOutEvent>(OnFocusOut);
		}

		private void HideDropdown()
		{
			if (!_dropdownVisible) return;
			_dropdownVisible = false;
			_mainButton.style.display = DisplayStyle.Flex;
			_searchField.style.display = DisplayStyle.None;
			_scrollView.style.display = DisplayStyle.None;
			_searchField.UnregisterCallback<FocusOutEvent>(OnFocusOut);
		}

		private void OnSearchTextChanged(ChangeEvent<string> evt)
		{
			FilterAndPopulateDropdown(evt.newValue);
		}

		private void FilterAndPopulateDropdown(string filterText)
		{
			if (string.IsNullOrEmpty(filterText))
			{
				_filteredChoices = _choices;
			}
			// cache optimization
			else if (!_searchCache.TryGetValue(filterText, out _filteredChoices))
			{
				// continuous typing optimization
				var pool = _filteredChoices != null && filterText[..^1].Equals(_filterText) ? _filteredChoices : _choices;
				_filteredChoices = FuzzySearch.Search(pool, filterText).ToArray();
				_searchCache[filterText] = _filteredChoices;
			}
			_filterText = filterText;

			_scrollView.Clear();
			_loadedItemCount = 0;
			PopulateMoreItems();
		}

		private void OnScroll(float value)
		{
			// Check if we are near the bottom of the scroll view
			if (_scrollView.verticalScroller.highValue - value < 10)
			{
				PopulateMoreItems();
			}
		}

		private void PopulateMoreItems()
		{
			if (_loadedItemCount >= _filteredChoices.Length)
			{
				return;
			}

			var itemsToAdd = _filteredChoices.Skip(_loadedItemCount).Take(MaxVisibleItems);

			foreach (var item in itemsToAdd)
			{
				var itemButton = new Button(() => OnItemClicked(Array.IndexOf(_choices, item)))
				{
					text = item,
					style =
					{
						borderTopWidth = 0, borderBottomWidth = 0,
						borderLeftWidth = 0, borderRightWidth = 0,
						borderTopLeftRadius = 0, borderTopRightRadius = 0,
						borderBottomLeftRadius = 0, borderBottomRightRadius = 0,
						marginTop = 0, marginBottom = 0,
						paddingTop = 3, paddingBottom = 3,
						justifyContent = Justify.FlexStart
					}
				};
				_scrollView.Add(itemButton);
			}
			_loadedItemCount += MaxVisibleItems;
		}

		private void OnItemClicked(int selectedIndex)
		{
			index = selectedIndex;
			HideDropdown();
		}
	}
}