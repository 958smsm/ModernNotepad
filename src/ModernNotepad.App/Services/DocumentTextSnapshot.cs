using System.Text;
using System.Windows.Documents;
using ModernNotepad.Core.Analysis;

namespace ModernNotepad.App.Services;

public sealed class DocumentTextSnapshot
{
    private readonly IReadOnlyList<TextPointer> _positions;

    private DocumentTextSnapshot(string text, IReadOnlyList<TextPointer> positions)
    {
        Text = text;
        _positions = positions;
    }

    public string Text { get; }

    public TextRange? CreateRange(TextSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.End > Text.Length)
        {
            return null;
        }

        return new TextRange(_positions[span.Start], _positions[span.End]);
    }

    public static DocumentTextSnapshot Create(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var builder = new SnapshotBuilder(document);
        return builder.Build();
    }

    private sealed class SnapshotBuilder
    {
        private readonly FlowDocument _document;
        private readonly StringBuilder _text = new();
        private readonly List<TextPointer> _positions = [];

        public SnapshotBuilder(FlowDocument document)
        {
            _document = document;
            _positions.Add(document.ContentStart);
        }

        public DocumentTextSnapshot Build()
        {
            AppendBlocks(_document.Blocks);
            if (_positions.Count != _text.Length + 1)
            {
                throw new InvalidOperationException("The document text map is inconsistent.");
            }

            return new DocumentTextSnapshot(_text.ToString(), _positions.ToArray());
        }

        private void AppendBlocks(IEnumerable<Block> blocks)
        {
            Block? previous = null;
            foreach (var block in blocks)
            {
                if (previous is not null)
                {
                    AppendLineBreak(previous.ContentEnd, block.ContentStart);
                }

                AppendBlock(block);
                previous = block;
            }
        }

        private void AppendBlock(Block block)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AppendInlines(paragraph.Inlines);
                    SetCurrentPointer(paragraph.ContentEnd);
                    break;
                case Section section:
                    AppendBlocks(section.Blocks);
                    SetCurrentPointer(section.ContentEnd);
                    break;
                case System.Windows.Documents.List list:
                    AppendList(list);
                    SetCurrentPointer(list.ContentEnd);
                    break;
                case Table table:
                    AppendTable(table);
                    SetCurrentPointer(table.ContentEnd);
                    break;
                case BlockUIContainer container:
                    AppendCharacter('\uFFFC', container.ContentEnd);
                    break;
                default:
                    SetCurrentPointer(block.ContentEnd);
                    break;
            }
        }

        private void AppendList(System.Windows.Documents.List list)
        {
            ListItem? previousItem = null;
            foreach (var item in list.ListItems)
            {
                if (previousItem is not null)
                {
                    AppendLineBreak(previousItem.ContentEnd, item.ContentStart);
                }

                AppendBlocks(item.Blocks);
                previousItem = item;
            }
        }

        private void AppendTable(Table table)
        {
            TableRow? previousRow = null;
            foreach (var rowGroup in table.RowGroups)
            {
                foreach (var row in rowGroup.Rows)
                {
                    if (previousRow is not null)
                    {
                        AppendLineBreak(previousRow.ContentEnd, row.ContentStart);
                    }

                    TableCell? previousCell = null;
                    foreach (var cell in row.Cells)
                    {
                        if (previousCell is not null)
                        {
                            AppendCharacter('\t', cell.ContentStart);
                        }

                        AppendBlocks(cell.Blocks);
                        previousCell = cell;
                    }

                    previousRow = row;
                }
            }
        }

        private void AppendInlines(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run:
                        AppendRun(run);
                        break;
                    case Span span:
                        AppendInlines(span.Inlines);
                        SetCurrentPointer(span.ContentEnd);
                        break;
                    case LineBreak lineBreak:
                        AppendLineBreak(lineBreak.ContentStart, lineBreak.ContentEnd);
                        break;
                    case InlineUIContainer container:
                        AppendCharacter('\uFFFC', container.ContentEnd);
                        break;
                    default:
                        SetCurrentPointer(inline.ContentEnd);
                        break;
                }
            }
        }

        private void AppendRun(Run run)
        {
            SetCurrentPointer(run.ContentStart);
            for (var index = 0; index < run.Text.Length; index++)
            {
                var after = run.ContentStart.GetPositionAtOffset(index + 1, LogicalDirection.Forward)
                    ?? run.ContentEnd;
                AppendCharacter(run.Text[index], after);
            }
        }

        private void AppendLineBreak(TextPointer before, TextPointer after)
        {
            SetCurrentPointer(before);
            AppendCharacter('\r', before);
            AppendCharacter('\n', after);
        }

        private void AppendCharacter(char character, TextPointer after)
        {
            _text.Append(character);
            _positions.Add(after);
        }

        private void SetCurrentPointer(TextPointer pointer)
        {
            _positions[^1] = pointer;
        }
    }
}
