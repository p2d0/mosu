{ pkgs ? import <nixpkgs> {config.allowUnfree = true;} }:

with pkgs;

mkShell {
  LD_LIBRARY_PATH = pkgs.lib.makeLibraryPath [
    ffmpeg
    alsa-lib
    SDL2
    lttng-ust
    numactl

    # needed to avoid:
    # Failed to create SDL window. SDL Error: Could not initialize OpenGL / GLES library
    libglvnd

    # needed for the window to actually appear
    xorg.libXi

    # needed to avoid in runtime.log:
    # [verbose]: SDL error log [debug]: Failed loading udev_device_get_action: /nix/store/*-osu-lazer-*/lib/osu-lazer/runtimes/linux-x64/native/libSDL2.so: undefined symbol: _udev_device_get_action
    # [verbose]: SDL error log [debug]: Failed loading libudev.so.1: libudev.so.1: cannot open shared object file: No such file or directory
    udev

    # needed for vulkan renderer, can fall back to opengl if omitted
    vulkan-loader
  ];
  SDL_VIDEODRIVER = "x11";
  DISPLAY = ":1";
  OSU_EXTERNAL_UPDATE_PROVIDER = 1;

  buildInputs = [
    pkgs.dotnetCorePackages.sdk_8_0_3xx
    pkgs.netcoredbg
    pkgs.csharp-ls
    # pkgs.omnisharp-roslyn
    # pkgs.roslyn-ls

  ];
}
