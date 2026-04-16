using System.Windows.Input;
using TaskManagementPr.Models;

namespace TaskManagementPr.Pages.Controls
{
    public partial class TaskView
    {
        public TaskView()
        {
            InitializeComponent();
        }

        public static readonly BindableProperty TaskCompletedCommandProperty = BindableProperty.Create(
            nameof(TaskCompletedCommand),
            typeof(ICommand),
            typeof(TaskView),
            null);

        public ICommand TaskCompletedCommand
        {
            get => (ICommand)GetValue(TaskCompletedCommandProperty);
            set => SetValue(TaskCompletedCommandProperty, value);
        }

        public static readonly BindableProperty TaskDeleteCommandProperty = BindableProperty.Create(
            nameof(TaskDeleteCommand),
            typeof(ICommand),
            typeof(TaskView),
            null);

        public ICommand TaskDeleteCommand
        {
            get => (ICommand)GetValue(TaskDeleteCommandProperty);
            set => SetValue(TaskDeleteCommandProperty, value);
        }

        public static readonly BindableProperty ShowQuickCompleteButtonProperty = BindableProperty.Create(
            nameof(ShowQuickCompleteButton),
            typeof(bool),
            typeof(TaskView),
            false);

        public bool ShowQuickCompleteButton
        {
            get => (bool)GetValue(ShowQuickCompleteButtonProperty);
            set => SetValue(ShowQuickCompleteButtonProperty, value);
        }

        public static readonly BindableProperty ShowDeleteButtonProperty = BindableProperty.Create(
            nameof(ShowDeleteButton),
            typeof(bool),
            typeof(TaskView),
            false);

        public bool ShowDeleteButton
        {
            get => (bool)GetValue(ShowDeleteButtonProperty);
            set => SetValue(ShowDeleteButtonProperty, value);
        }

        private void CheckBox_CheckedChanged(object? sender, CheckedChangedEventArgs e)
        {
            var checkbox = (CheckBox?)sender;

            if (checkbox?.BindingContext is not ProjectTask task)
                return;

            if (task.IsCompleted == e.Value)
                return;

            task.IsCompleted = e.Value;
            TaskCompletedCommand?.Execute(task);
        }

        private void QuickComplete_OnClicked(object? sender, EventArgs e)
        {
            if (BindingContext is not ProjectTask task)
                return;

            if (task.IsCompleted)
                return;

            task.IsCompleted = true;
            TaskCompletedCommand?.Execute(task);
        }

        private void Delete_OnClicked(object? sender, EventArgs e)
        {
            if (BindingContext is not ProjectTask task)
                return;

            TaskDeleteCommand?.Execute(task);
        }
    }
}
