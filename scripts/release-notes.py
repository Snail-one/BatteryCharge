#!/usr/bin/env python3
"""Write a commit-log preface for GitHub's generated release notes."""

import argparse
import os
from pathlib import Path
import re
import subprocess


def git(*arguments):
    return subprocess.run(
        ["git", *arguments], check=True, capture_output=True,
        encoding="utf-8", errors="replace",
    ).stdout.strip()


def markdown(text):
    return re.sub(r"([\\`*_{}\[\]<>()#!|])", r"\\\1", text)


def generate(tag, repository, output):
    commit = git("rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}")
    previous = subprocess.run(
        ["git", "describe", "--first-parent", "--tags", "--abbrev=0", f"{commit}^"],
        capture_output=True, encoding="utf-8", errors="replace",
    )
    previous_tag = previous.stdout.strip() if previous.returncode == 0 else ""
    revision = f"{previous_tag}..{commit}" if previous_tag else commit
    records = git("log", "--first-parent", "--no-merges", "--reverse", "--encoding=UTF-8",
                  "--format=%H%x00%h%x00%s%x00%an", revision)
    lines = ["## 提交记录", ""]
    if previous_tag:
        lines.extend([f"范围：{markdown(previous_tag)} → {markdown(tag)}。", ""])
    else:
        lines.extend(["首次发布，列出当前版本的主线提交。", ""])
    for record in records.splitlines():
        full_hash, short_hash, subject, author = record.split("\0", 3)
        lines.append(f"- {markdown(subject)} — {markdown(author)} "
                     f"([{short_hash}](https://github.com/{repository}/commit/{full_hash}))")
    if not records:
        lines.append("此范围没有主线非合并提交；Pull Request 变更见下方 GitHub 生成的说明。")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text("\n".join(lines) + "\n\n", encoding="utf-8")
    return previous_tag


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    previous_tag = generate(arguments.tag, arguments.repository, arguments.output)
    if github_output := os.environ.get("GITHUB_OUTPUT"):
        with open(github_output, "a", encoding="utf-8") as stream:
            stream.write(f"previous_tag={previous_tag}\n")
    print(f"Commit notes: {arguments.output}; previous tag: {previous_tag or '(first release)'}")


if __name__ == "__main__":
    main()
