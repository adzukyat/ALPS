using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The step after adding fixtures, at the foot of the container inspector. A top
    /// container holding fixtures that nothing plays yet offers to set up a Timeline around it
    /// (<see cref="AlpsTimelineSetup"/>), one a director already plays offers to open that
    /// Timeline, and any other container shows nothing here.
    /// </summary>
    public class AlpsContainerSetupRow : VisualElement
    {
        private readonly AlpsContainer _container;
        private readonly VisualElement _button;
        private readonly Label _title;

        public AlpsContainerSetupRow(AlpsContainer container)
        {
            _container = container;
            AddToClassList("alps-setup");

            _button = new VisualElement();
            _button.AddToClassList("alps-add__button");
            _title = new Label();
            _title.AddToClassList("alps-add__title");
            _button.Add(_title);
            _button.RegisterCallback<PointerDownEvent>(_ => Run());
            Add(_button);

            Refresh();
        }

        /// <summary>What the button does now.</summary>
        public AlpsContainerSetupStep Step { get; private set; }

        /// <summary>The button's caption, for tests.</summary>
        public string Title => _title.text;

        public void Refresh()
        {
            if (_container == null)
            {
                Step = AlpsContainerSetupStep.None;
            }
            else if (AlpsTimelineSetup.FindDirector(_container) != null)
            {
                Step = AlpsContainerSetupStep.Open;
            }
            else
            {
                Step = AlpsTimelineSetup.CanSetUp(_container) ? AlpsContainerSetupStep.SetUp : AlpsContainerSetupStep.None;
            }

            style.display = Step == AlpsContainerSetupStep.None ? DisplayStyle.None : DisplayStyle.Flex;

            // Setting up is the step the container waits for, so it stands out like an open list.
            _button.EnableInClassList("alps-add__button--open", Step == AlpsContainerSetupStep.SetUp);
            _title.text = Step == AlpsContainerSetupStep.Open ? AlpsStrings.Tr("setup.openTimeline") : AlpsStrings.Tr("setup.timeline");
            _button.tooltip = Step == AlpsContainerSetupStep.Open ? AlpsStrings.Tr("setup.openTimeline.tip") : AlpsStrings.Tr("setup.timeline.tip");
        }

        private void Run()
        {
            Refresh();
            switch (Step)
            {
                case AlpsContainerSetupStep.SetUp:
                    AlpsTimelineSetup.Open(AlpsTimelineSetup.SetUp(_container));
                    break;
                case AlpsContainerSetupStep.Open:
                    AlpsTimelineSetup.Open(AlpsTimelineSetup.FindDirector(_container));
                    break;
            }

            Refresh();
        }
    }

    public enum AlpsContainerSetupStep
    {
        None,
        SetUp,
        Open,
    }
}
