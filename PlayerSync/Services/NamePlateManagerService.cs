using Dalamud.Game.Gui.NamePlate;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using MareSynchronos.API.Data.Extensions;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace PlayerSync.Services
{
    public class NamePlateManagerService : DisposableMediatorSubscriberBase, IHostedService
    {
        private readonly INamePlateGui _namePlateGui;
        private readonly PairManager _pairs;
        private readonly ILogger<NamePlateManagerService> _logger;
        private readonly MareConfigService _configService;
        private const ushort UiColorId = 41;

        public NamePlateManagerService(ILogger<NamePlateManagerService> logger, MareMediator mediator, INamePlateGui namePlateGui, 
            PairManager pairManager, MareConfigService mareConfigService) : base(logger, mediator)
        {
            _namePlateGui = namePlateGui;
            _pairs = pairManager;
            _logger = logger;
            _configService = mareConfigService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("NamePlaterManager started.");
            _namePlateGui.OnNamePlateUpdate += UpdateNamePlate;
            Mediator.Subscribe<RedrawNameplateMessage>(this, (_) => _namePlateGui.RequestRedraw());
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("Stopping NamePlateManager");
            _namePlateGui.OnNamePlateUpdate -= UpdateNamePlate;
            return Task.CompletedTask;
        }

        private void UpdateNamePlate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
        {
            if (!(_configService.Current.ShowPairedIndicator 
                || _configService.Current.ShowPermsInsteadOfFCTags 
                || _configService.Current.ShowSoundSourceIndicator
                || _configService.Current.ShowNameHighlights)) return;

            var visiblePairs = _pairs.GetVisiblePairs();
            var visibleByAddress = visiblePairs.Where(p => p.Address != nint.Zero).ToImmutableDictionary(p => p.Address, p => p);

            foreach (var handle in handlers)
            {
                if (handle.NamePlateKind != NamePlateKind.PlayerCharacter) continue;
                var addr = handle.PlayerCharacter?.Address ?? nint.Zero;
                if (addr == nint.Zero) continue;
                if (handle.PlayerCharacter?.ObjectIndex is null or 0) continue;
                if (!visibleByAddress.TryGetValue(addr, out var pair)) continue;
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
                else
                {
                    fcTagBuilder.Append(handle.FreeCompanyTag);
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
                    var textColor = MakeOpaque(color.Foreground);
                    var textGlow = MakeOpaque(color.Glow);

                    // workaround for now since 0 can mess with honorifics
                    if (textGlow == 0) textGlow = 4278255873;
                    handle.TextColor = textColor;
                    handle.EdgeColor = textGlow;
                }
            }
        }

        private unsafe static bool IsFriend(INamePlateUpdateHandler handler) => ((Character*)handler.PlayerCharacter!.Address)->IsFriend;

        private static uint MakeOpaque(uint rgb)
        {
            if (rgb == 0) return 0;
            return (rgb & 0x00FFFFFF) | 0xFF000000; // ensure AA = 0xFF
        }
    }
}
