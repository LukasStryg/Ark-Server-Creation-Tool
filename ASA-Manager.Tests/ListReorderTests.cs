using System.Collections.Generic;
using ARKServerCreationTool.Services.Common;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ListReorderTests
    {
        [Fact]
        public void MoveUp_swaps_with_previous()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveUp(list, 2);
            Assert.Equal(new[] { 1, 3, 2 }, list);
        }

        [Fact]
        public void MoveUp_at_top_is_noop()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveUp(list, 0);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void MoveDown_swaps_with_next()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveDown(list, 0);
            Assert.Equal(new[] { 2, 1, 3 }, list);
        }

        [Fact]
        public void MoveDown_at_bottom_is_noop()
        {
            var list = new List<int> { 1, 2, 3 };
            ListReorder.MoveDown(list, 2);
            Assert.Equal(new[] { 1, 2, 3 }, list);
        }

        [Fact]
        public void Move_relocates_item_preserving_others()
        {
            var list = new List<int> { 1, 2, 3, 4 };
            ListReorder.Move(list, 0, 2);
            Assert.Equal(new[] { 2, 3, 1, 4 }, list);
        }
    }
}
