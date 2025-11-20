using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.PlayerData.Pairs;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using MareSynchronos.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.MareConfiguration;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using System.Reflection.Metadata;

namespace PlayerSync.Services
{
    public class NamePlateManagerService : MediatorSubscriberBase, IDisposable
    {
        private readonly INamePlateGui _namePlateGui;
        private readonly PairManager _pairs;
        private readonly ILogger<NamePlateManagerService> _logger;
        private readonly DalamudUtilService _dalamudUtil;
        private readonly MareConfigService _configService;
        private DateTime _lastUpdate = DateTime.MinValue;
        private ImmutableList<Pair> _cachedVisiblePairs = ImmutableList<Pair>.Empty;
        private ImmutableDictionary<nint, Pair> _visibleByAddress = ImmutableDictionary<nint, Pair>.Empty;
        private const ushort UiColorId = 41;

        public NamePlateManagerService(ILogger<NamePlateManagerService> logger, MareMediator mediator, INamePlateGui namePlateGui, 
            PairManager pairManager, DalamudUtilService dalamudUtilService, MareConfigService mareConfigService) : base(logger, mediator)
        {
            _namePlateGui = namePlateGui;
            _pairs = pairManager;
            _logger = logger;
            _dalamudUtil = dalamudUtilService;
            _configService = mareConfigService;

            _namePlateGui.OnNamePlateUpdate += UpdateNamePlate;

            Mediator.Subscribe<RedrawNameplateMessage>(this, (_) => _namePlateGui.RequestRedraw());

            _logger.LogDebug("NamePlaterManager started.");
        }

        private string Self => _dalamudUtil.GetPlayerName();

        private static ImmutableList<Pair> ImmutablePairList(IEnumerable<KeyValuePair<Pair, List<GroupFullInfoDto>>> u) => u.Select(k => k.Key).ToImmutableList();

        private void UpdateNamePlate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
        {
            // We could probably make this into an IHostedService and just stop/start the service, unsure.
            if (!(_configService.Current.ShowPairedIndicator 
                || _configService.Current.ShowPermsInsteadOfFCTags 
                || _configService.Current.ShowSoundSourceIndicator
                || _configService.Current.ShowNameHighlights)) return;

            var shouldUpdate = false;
            var now = DateTime.UtcNow;
            if ((now - _lastUpdate).TotalMilliseconds > 250)
            {
                _lastUpdate = now;
                shouldUpdate = true;
            }

            if (shouldUpdate)
            {
                var allPairs = _pairs.PairsWithGroups.ToDictionary(k => k.Key, k => k.Value);
                bool FilterVisibleUsers(KeyValuePair<Pair, List<GroupFullInfoDto>> u) => u.Key.IsVisible;
                _cachedVisiblePairs = ImmutablePairList(allPairs.Where(FilterVisibleUsers));
                _visibleByAddress = _cachedVisiblePairs.Where(p => p.Address != nint.Zero).ToImmutableDictionary(p => p.Address, p => p);
            }

            foreach (var handle in handlers)
            {
                if (handle.NamePlateKind != NamePlateKind.PlayerCharacter) continue;
                var addr = handle.PlayerCharacter?.Address ?? nint.Zero;
                if (addr == nint.Zero) continue;
                if (handle.PlayerCharacter?.Name.TextValue == Self) continue;
                if (!_visibleByAddress.TryGetValue(addr, out var pair)) continue;
                var color = _configService.Current.NameHighlightColor;
                var fcTagBuilder = new SeStringBuilder();

                if (_configService.Current.ShowPermsInsteadOfFCTags)
                {
                    var permsSelf = pair.UserPair.OwnPermissions;
                    var permsOther = pair.UserPair.OtherPermissions;
                    var isDisabledSounds = permsSelf.IsDisableSounds() || permsOther.IsDisableSounds();
                    var isDisabledAnimations = permsSelf.IsDisableAnimations() || permsOther.IsDisableAnimations();
                    var isDisabledVfx = permsSelf.IsDisableVFX() || permsOther.IsDisableVFX();
                    var colorDisabled = _configService.Current.PermsColorsDisabled;
                    var colorEnabled = _configService.Current.PermsColorsEnabled;

                    fcTagBuilder.Append(" «");
                    // sounds
                    fcTagBuilder.AddColoredText("", isDisabledSounds ? colorDisabled : colorEnabled);
                    // animations
                    fcTagBuilder.AddColoredText("", isDisabledAnimations ? colorDisabled : colorEnabled);
                    // vfx
                    fcTagBuilder.AddColoredText("", isDisabledVfx ? colorDisabled : colorEnabled);
                    fcTagBuilder.Append("»");
                }

                if (_configService.Current.ShowPairedIndicator)
                {
                    fcTagBuilder.AddUiForeground(UiColorId);
                    fcTagBuilder.Append(" ⇔");
                    fcTagBuilder.AddUiForegroundOff();
                }

                handle.FreeCompanyTag = fcTagBuilder.Build();

                if (_configService.Current.ShowNameHighlights && (!IsFriend(handle) || _configService.Current.IncludeFriendHighlights))
                {
                    handle.TextColor = color.Foreground;
                    handle.EdgeColor = color.Glow;
                }
            }
        }

        public void Dispose()
        {
            _logger.LogTrace("Disposing of NamePlateManager");
            _namePlateGui.OnNamePlateUpdate -= UpdateNamePlate;
        }

        private unsafe static bool IsFriend(INamePlateUpdateHandler handler) => ((Character*)handler.PlayerCharacter!.Address)->IsFriend;
    }
}
