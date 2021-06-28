using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SertCheck.Models
{
    [Table("dd_documents", Schema = "core")]
    public class Document
    {
        [Key]
        public Guid id { get; set; }
        public string c_first_name { get; set; }
        public string c_last_name { get; set; }
        public string c_middle_name { get; set; }
        public DateTime? d_birthday { get; set; }

        /// <summary>
        /// Муниципалитет
        /// </summary>
        public int f_user { get; set; }
        public bool sn_delete { get; set; }
        public DateTime dx_created { get; set; }
        public bool b_ignore { get; set; }
    }
}
