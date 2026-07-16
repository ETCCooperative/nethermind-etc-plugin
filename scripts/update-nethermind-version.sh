#!/usr/bin/env bash
#
# Bump the plugin to a new Nethermind version.
#
# Besides the version bump itself, this aligns the dependencies we share with
# Nethermind to the versions that release was built against. Nethermind exposes
# some of them (Autofac) through its public API, so a mismatch fails the build
# with CS1705 even when our own code needs no changes.
#
# Usage: scripts/update-nethermind-version.sh <current-version> <new-version>

set -euo pipefail

CURRENT_VERSION="${1:?usage: $0 <current-version> <new-version>}"
NEW_VERSION="${2:?usage: $0 <current-version> <new-version>}"

PROPS="Directory.Packages.props"
CSPROJ="src/Nethermind.EthereumClassic/Nethermind.EthereumClassic.csproj"
NETHERMIND_REPO="${NETHERMIND_REPO:-NethermindEth/nethermind}"

# Packages both Nethermind and the plugin reference, which must stay in lockstep.
# Test-only packages are deliberately excluded: they never meet Nethermind across
# an assembly boundary, so pinning those ourselves is fine.
SHARED_PACKAGES=(Autofac Nethermind.Numerics.Int256)

# Package names are matched literally. Names ending in digits (Int256) would
# otherwise let a loose pattern capture part of the name as the version.
pkg_version() { # <file> <package>
  PKG="$2" perl -ne '
    my $pkg = quotemeta($ENV{PKG});
    print "$1\n" and exit if /Include="$pkg" Version="([^"]+)"/;
  ' "$1"
}

set_pkg_version() { # <file> <package> <version>
  PKG="$2" VERSION="$3" perl -pi -e '
    my $pkg = quotemeta($ENV{PKG});
    s/(Include="$pkg" Version=")[^"]+(")/$1$ENV{VERSION}$2/;
  ' "$1"
}

echo "Updating Nethermind $CURRENT_VERSION -> $NEW_VERSION"

set_pkg_version "$PROPS" "Nethermind.ReferenceAssemblies" "$NEW_VERSION"
CURRENT_VERSION="$CURRENT_VERSION" NEW_VERSION="$NEW_VERSION" perl -pi -e '
  my $cur = quotemeta($ENV{CURRENT_VERSION});
  s|<Version>$cur\.0</Version>|<Version>$ENV{NEW_VERSION}.0</Version>|;
' "$CSPROJ"

# Fail loudly rather than silently releasing the old version: a no-op bump means
# the file layout drifted from what the patterns above expect.
got=$(pkg_version "$PROPS" "Nethermind.ReferenceAssemblies")
[ "$got" = "$NEW_VERSION" ] || {
  echo "ERROR: $PROPS still pins Nethermind.ReferenceAssemblies $got after the bump" >&2
  exit 1
}
grep -q "<Version>$NEW_VERSION.0</Version>" "$CSPROJ" || {
  echo "ERROR: $CSPROJ was not bumped to $NEW_VERSION.0" >&2
  exit 1
}

echo "Aligning shared dependencies with Nethermind $NEW_VERSION"
UPSTREAM_PROPS=$(mktemp)
trap 'rm -f "$UPSTREAM_PROPS"' EXIT
URL="https://raw.githubusercontent.com/$NETHERMIND_REPO/$NEW_VERSION/Directory.Packages.props"

if curl -fsSL "$URL" -o "$UPSTREAM_PROPS"; then
  for pkg in "${SHARED_PACKAGES[@]}"; do
    theirs=$(pkg_version "$UPSTREAM_PROPS" "$pkg")
    ours=$(pkg_version "$PROPS" "$pkg")
    if [ -z "$theirs" ]; then
      echo "  $pkg: not declared upstream, left at $ours"
    elif [ "$theirs" = "$ours" ]; then
      echo "  $pkg: already aligned at $ours"
    else
      set_pkg_version "$PROPS" "$pkg" "$theirs"
      echo "  $pkg: $ours -> $theirs"
    fi
  done
else
  # Not fatal: if this leaves a dependency stale the build fails below and the
  # workflow opens a PR, which is the same outcome as before this check existed.
  echo "WARNING: could not fetch $URL — shared dependencies not verified" >&2
fi

echo
echo "Resulting versions:"
grep -E 'PackageVersion Include' "$PROPS"
grep -E '<Version>' "$CSPROJ"
