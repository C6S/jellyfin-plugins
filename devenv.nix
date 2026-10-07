{
  pkgs,
  config,
  ...
}:
{
  packages = with pkgs; [
    dotnet-sdk_10
    git
    just
    jq
  ];

  # Mark regenerable state so backup tools skip it (https://bford.info/cachedir/).
  enterShell = ''
    for dir in "${config.devenv.dotfile}" "${config.devenv.root}/.direnv"; do
      if [ -d "$dir" ] && [ ! -e "$dir/CACHEDIR.TAG" ]; then
        printf 'Signature: 8a477f597d28d172789f06886806bc55\n' > "$dir/CACHEDIR.TAG"
      fi
    done
  '';
}
