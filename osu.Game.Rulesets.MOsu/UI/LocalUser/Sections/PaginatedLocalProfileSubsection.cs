// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Graphics.Containers;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osuTK;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Overlays.Profile;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser.Sections
{
    public abstract partial class PaginatedLocalProfileSubsection<TModel> : ProfileSubsection
    {
        /// <summary>
        /// The number of items displayed per page.
        /// </summary>
        protected virtual int ItemsPerPage => 50;

        /// <summary>
        /// The number of items displayed initially.
        /// </summary>
        protected virtual int InitialItemsCount => 5;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        protected PaginationParameters? CurrentPage { get; private set; }

        protected ReverseChildIDFillFlowContainer<Drawable> ItemsContainer { get; private set; } = null!;

        private List<TModel>? taskResult;
        private CancellationTokenSource? loadCancellation;

        private ShowMoreButton moreButton = null!;
        private OsuSpriteText missing = null!;
        private readonly LocalisableString? missingText;

        protected PaginatedLocalProfileSubsection(Bindable<UserProfileData?> user, LocalisableString? headerText = null, LocalisableString? missingText = null)
            : base(user, headerText, CounterVisibilityState.AlwaysVisible)
        {
            this.missingText = missingText;
        }

        protected override Drawable CreateContent() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                // reverse ID flow is required for correct Z-ordering of the items (last item should be front-most).
                // particularly important in PaginatedBeatmapContainer, as it uses beatmap cards, which have expandable overhanging content.
                ItemsContainer = new ReverseChildIDFillFlowContainer<Drawable>
                {
                    AutoSizeAxes = Axes.Y,
                    RelativeSizeAxes = Axes.X,
                    Spacing = new Vector2(0, 2),
                    // ensure the container and its contents are in front of the "more" button.
                    Depth = float.MinValue
                },
                moreButton = new ShowMoreButton
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Alpha = 0,
                    Margin = new MarginPadding { Top = 10 },
                    Action = showMore,
                },
                missing = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 15),
                    Text = missingText ?? string.Empty,
                    Alpha = 0,
                }
            }
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();
            User.BindValueChanged(onUserChanged, true);
        }

        private async void onUserChanged(ValueChangedEvent<UserProfileData?> e)
        {
            loadCancellation?.Cancel();
            // retrievalTask?.Cancel();

            CurrentPage = null;
            ItemsContainer.Clear();

            if (e.NewValue?.User != null)
            {
                Logger.Log("Creating taskResult", level: LogLevel.Debug);
                taskResult = await CreateTask(User.Value).ConfigureAwait(false);
                showMore();
                SetCount(GetCount(e.NewValue.User));
            }
        }

        private void showMore()
        {
            Logger.Log("Show more clicked", level: LogLevel.Debug);
            if (User.Value == null)
                return;
            // if(taskResult == null){
            //     Logger.Log("Creating taskResult", level: LogLevel.Debug);
            //     taskResult = await CreateTask(User.Value);
            // }

            Logger.Log($"Total items count: {taskResult?.Count ?? 0}", level: LogLevel.Debug);
            loadCancellation = new CancellationTokenSource();

            CurrentPage = CurrentPage?.TakeNext(ItemsPerPage) ?? new PaginationParameters(InitialItemsCount);

            UpdateItems(loadCancellation);
        }

        protected virtual void UpdateItems(CancellationTokenSource cancellationTokenSource) => Schedule(() =>
        {
            if (taskResult == null || CurrentPage == null)
                return;

            if (!taskResult.Any() && CurrentPage.Value.Offset == 0)
            {
                moreButton.Hide();
                moreButton.IsLoading = false;

                if (missingText.HasValue)
                    missing.Show();

                return;
            }

            var paginatedItems = taskResult.Skip(CurrentPage.Value.Offset).Take(CurrentPage.Value.Limit).ToList();
            Logger.Log($"Paginated items count: {paginatedItems.Count}", level: LogLevel.Debug);

            bool hasMore = taskResult.Count > CurrentPage.Value.Limit;
            Logger.Log($"Has more: {hasMore}", level: LogLevel.Debug);

            OnItemsReceived(paginatedItems);

            LoadComponentsAsync(paginatedItems.Select(CreateDrawableItem).Where(d => d != null).Cast<Drawable>(), drawables =>
            {
                missing.Hide();

                moreButton.FadeTo(hasMore ? 1 : 0);
                moreButton.IsLoading = false;

                ItemsContainer.AddRange(drawables);
            }, cancellationTokenSource.Token);
        });

        protected virtual int GetCount(APIUser user) => 0;

        protected virtual void OnItemsReceived(List<TModel> items)
        {
        }

        protected abstract Task<List<TModel>> CreateTask(UserProfileData? user);

        protected abstract Drawable? CreateDrawableItem(TModel model);

        protected override void Dispose(bool isDisposing)
        {
            // retrievalTask?.Cancel();
            loadCancellation?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
