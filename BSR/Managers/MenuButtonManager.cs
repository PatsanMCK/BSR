using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BSR.UI;
using Zenject;

namespace BSR.Managers
{
    public class MenuButtonManager : IInitializable, IDisposable
    {
        private readonly MenuButton _menuButton;
        private readonly RankedFlowCoordinator _flowCoordinator;
        private readonly MenuButtons _menuButtons;

        public MenuButtonManager(RankedFlowCoordinator flowCoordinator, MenuButtons menuButtons)
        {
            _flowCoordinator = flowCoordinator;
            _menuButtons = menuButtons;
            _menuButton = new MenuButton("BS Ranked", "1v1 Ranked similar to osu!", OnMenuButtonClick, true);
        }

        public void Initialize() => _menuButtons.RegisterButton(_menuButton);

        public void Dispose() => _menuButtons.UnregisterButton(_menuButton);

        private void OnMenuButtonClick()
        {
            BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
        }
    }
}