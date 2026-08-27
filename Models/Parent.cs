using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    public class Parent
    {
        [Key]
        public int ParentId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        public ICollection<ParentLearner> Children { get; set; } = new List<ParentLearner>();
    }
}
